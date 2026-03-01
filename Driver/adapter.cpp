/*
 * SoundBox Virtual Audio Device - Adapter (DriverEntry + Device Setup)
 *
 * This is a PortCls-based virtual audio capture device driver.
 * It creates a virtual microphone that receives audio data from
 * the SoundBox user-mode application via shared memory.
 *
 * Build requirements: Visual Studio + WDK (Windows Driver Kit)
 */

#include <ntddk.h>
#include <portcls.h>
#include <ksdebug.h>
#include "common.h"
#include "miniport.h"

// Forward declarations
DRIVER_ADD_DEVICE AddDevice;

extern "C" DRIVER_INITIALIZE DriverEntry;

#ifdef ALLOC_PRAGMA
#pragma alloc_text(INIT, DriverEntry)
#pragma alloc_text(PAGE, AddDevice)
#endif

// Global shared memory handles
HANDLE         g_SectionHandle = NULL;
PVOID          g_SharedBuffer  = NULL;
HANDLE         g_EventHandle   = NULL;
PKEVENT        g_DataReadyEvent = NULL;

// Create shared memory section accessible from user-mode
static NTSTATUS CreateSharedMemory()
{
    UNICODE_STRING sectionName;
    OBJECT_ATTRIBUTES objAttr;
    LARGE_INTEGER sectionSize;
    NTSTATUS status;

    RtlInitUnicodeString(&sectionName, SOUNDBOX_SECTION_NAME);
    InitializeObjectAttributes(&objAttr, &sectionName,
        OBJ_CASE_INSENSITIVE | OBJ_KERNEL_HANDLE, NULL, NULL);

    // Create a security descriptor that allows user-mode access
    SECURITY_DESCRIPTOR sd;
    status = RtlCreateSecurityDescriptor(&sd, SECURITY_DESCRIPTOR_REVISION);
    if (!NT_SUCCESS(status)) return status;

    status = RtlSetDaclSecurityDescriptor(&sd, TRUE, NULL, FALSE);
    if (!NT_SUCCESS(status)) return status;

    objAttr.SecurityDescriptor = &sd;

    sectionSize.QuadPart = SOUNDBOX_DATA_OFFSET + SOUNDBOX_RING_BUFFER_SIZE;

    status = ZwCreateSection(&g_SectionHandle, SECTION_ALL_ACCESS,
        &objAttr, &sectionSize, PAGE_READWRITE, SEC_COMMIT, NULL);
    if (!NT_SUCCESS(status)) return status;

    // Map section into kernel address space
    SIZE_T viewSize = 0;
    status = ZwMapViewOfSection(g_SectionHandle, ZwCurrentProcess(),
        &g_SharedBuffer, 0, 0, NULL, &viewSize, ViewUnmap, 0, PAGE_READWRITE);
    if (!NT_SUCCESS(status))
    {
        ZwClose(g_SectionHandle);
        g_SectionHandle = NULL;
        return status;
    }

    // Initialize the shared buffer header
    SOUNDBOX_SHARED_BUFFER* header = (SOUNDBOX_SHARED_BUFFER*)g_SharedBuffer;
    RtlZeroMemory(header, viewSize);
    header->BufferSize     = SOUNDBOX_RING_BUFFER_SIZE;
    header->SampleRate     = SOUNDBOX_SAMPLE_RATE;
    header->Channels       = SOUNDBOX_CHANNELS;
    header->BitsPerSample  = SOUNDBOX_BITS_PER_SAMPLE;

    return STATUS_SUCCESS;
}

// Create named event for data signaling
static NTSTATUS CreateSignalEvent()
{
    UNICODE_STRING eventName;
    OBJECT_ATTRIBUTES objAttr;
    SECURITY_DESCRIPTOR sd;
    NTSTATUS status;

    RtlInitUnicodeString(&eventName, SOUNDBOX_EVENT_NAME);

    status = RtlCreateSecurityDescriptor(&sd, SECURITY_DESCRIPTOR_REVISION);
    if (!NT_SUCCESS(status)) return status;
    status = RtlSetDaclSecurityDescriptor(&sd, TRUE, NULL, FALSE);
    if (!NT_SUCCESS(status)) return status;

    InitializeObjectAttributes(&objAttr, &eventName,
        OBJ_CASE_INSENSITIVE | OBJ_KERNEL_HANDLE, NULL, &sd);

    status = ZwCreateEvent(&g_EventHandle, EVENT_ALL_ACCESS,
        &objAttr, SynchronizationEvent, FALSE);
    if (!NT_SUCCESS(status)) return status;

    status = ObReferenceObjectByHandle(g_EventHandle, EVENT_ALL_ACCESS,
        *ExEventObjectType, KernelMode, (PVOID*)&g_DataReadyEvent, NULL);

    return status;
}

// Cleanup shared resources
static void CleanupSharedResources()
{
    if (g_DataReadyEvent)
    {
        ObDereferenceObject(g_DataReadyEvent);
        g_DataReadyEvent = NULL;
    }
    if (g_EventHandle)
    {
        ZwClose(g_EventHandle);
        g_EventHandle = NULL;
    }
    if (g_SharedBuffer)
    {
        ZwUnmapViewOfSection(ZwCurrentProcess(), g_SharedBuffer);
        g_SharedBuffer = NULL;
    }
    if (g_SectionHandle)
    {
        ZwClose(g_SectionHandle);
        g_SectionHandle = NULL;
    }
}

// StartDevice callback - creates port/miniport pair
static NTSTATUS StartDevice(
    PDEVICE_OBJECT DeviceObject,
    PIRP           Irp,
    PRESOURCELIST  ResourceList)
{
    PAGED_CODE();
    NTSTATUS status;

    // Create the WaveRT port
    PUNKNOWN pPortUnknown = NULL;
    status = PcNewPort(&pPortUnknown, CLSID_PortWaveRT);
    if (!NT_SUCCESS(status)) return status;

    // Create our miniport
    PUNKNOWN pMiniportUnknown = NULL;
    status = CreateSoundBoxMiniport(&pMiniportUnknown);
    if (!NT_SUCCESS(status))
    {
        pPortUnknown->Release();
        return status;
    }

    // Register the port/miniport pair as a capture device
    status = PcRegisterSubdevice(DeviceObject, L"SoundBoxCapture",
        pPortUnknown, pMiniportUnknown);

    pPortUnknown->Release();
    pMiniportUnknown->Release();

    return status;
}

// AddDevice - PnP device setup
NTSTATUS AddDevice(
    PDRIVER_OBJECT  DriverObject,
    PDEVICE_OBJECT  PhysicalDeviceObject)
{
    PAGED_CODE();
    return PcAddAdapterDevice(DriverObject, PhysicalDeviceObject,
        StartDevice, MAX_MINIPORTS, 0);
}

// Driver unload
static void DriverUnload(PDRIVER_OBJECT DriverObject)
{
    CleanupSharedResources();
}

// DriverEntry - main entry point
extern "C" NTSTATUS DriverEntry(
    PDRIVER_OBJECT  DriverObject,
    PUNICODE_STRING RegistryPath)
{
    NTSTATUS status;

    // Initialize shared memory and event
    status = CreateSharedMemory();
    if (!NT_SUCCESS(status)) return status;

    status = CreateSignalEvent();
    if (!NT_SUCCESS(status))
    {
        CleanupSharedResources();
        return status;
    }

    // Initialize PortCls
    status = PcInitializeAdapterDriver(DriverObject, RegistryPath, AddDevice);
    if (!NT_SUCCESS(status))
    {
        CleanupSharedResources();
        return status;
    }

    DriverObject->DriverUnload = DriverUnload;

    return STATUS_SUCCESS;
}
