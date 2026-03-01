/*
 * SoundBox Virtual Audio Device - WaveRT Miniport
 *
 * Implements a virtual microphone capture device.
 * Audio data comes from user-mode via shared memory.
 */

#include <ntddk.h>
#include <portcls.h>
#include <ksdebug.h>
#include "miniport.h"
#include "common.h"

// ============================================================
// Audio format descriptors
// ============================================================

// Supported format: 48kHz, 16-bit, Stereo
static KSDATARANGE_AUDIO g_PinDataRangeCapture =
{
    {
        sizeof(KSDATARANGE_AUDIO),
        0,
        0,
        0,
        STATICGUIDOF(KSDATAFORMAT_TYPE_AUDIO),
        STATICGUIDOF(KSDATAFORMAT_SUBTYPE_PCM),
        STATICGUIDOF(KSDATAFORMAT_SPECIFIER_WAVEFORMATEX)
    },
    SOUNDBOX_CHANNELS,       // MaximumChannels
    SOUNDBOX_BITS_PER_SAMPLE,// MinimumBitsPerSample
    SOUNDBOX_BITS_PER_SAMPLE,// MaximumBitsPerSample
    SOUNDBOX_SAMPLE_RATE,    // MinimumSampleFrequency
    SOUNDBOX_SAMPLE_RATE     // MaximumSampleFrequency
};

static PKSDATARANGE g_PinDataRanges[] = { (PKSDATARANGE)&g_PinDataRangeCapture };

// Pin descriptor - one capture pin
static PCPIN_DESCRIPTOR g_PinDescriptors[] =
{
    {
        1, 1, 0,  // MaxGlobalInstanceCount, MaxFilterInstanceCount, MinFilterInstanceCount
        NULL,     // AutomationTable
        {
            0,                              // InterfacesCount
            NULL,                           // Interfaces
            0,                              // MediumsCount
            NULL,                           // Mediums
            SIZEOF_ARRAY(g_PinDataRanges),  // DataRangesCount
            g_PinDataRanges,                // DataRanges
            KSPIN_DATAFLOW_OUT,             // DataFlow (OUT = capture)
            KSPIN_COMMUNICATION_SINK,       // Communication
            &KSCATEGORY_AUDIO,              // Category
            NULL,                           // Name
            0                               // Reserved
        }
    }
};

// Filter descriptor
static PCFILTER_DESCRIPTOR g_FilterDescriptor =
{
    0,                                    // Version
    NULL,                                 // AutomationTable
    sizeof(PCPIN_DESCRIPTOR),             // PinSize
    SIZEOF_ARRAY(g_PinDescriptors),       // PinCount
    g_PinDescriptors,                     // Pins
    0,                                    // NodeSize
    0,                                    // NodeCount
    NULL,                                 // Nodes
    0,                                    // ConnectionCount
    NULL,                                 // Connections
    0,                                    // CategoryCount
    NULL                                  // Categories
};

// ============================================================
// CMiniportWaveRT - WaveRT Miniport implementation
// ============================================================
class CMiniportWaveRT : public IMiniportWaveRT, public CUnknown
{
private:
    PPORTWAVERT m_Port;

public:
    DECLARE_STD_UNKNOWN();
    DEFINE_STD_CONSTRUCTOR(CMiniportWaveRT);

    IMP_IMiniportWaveRT;

    ~CMiniportWaveRT();
};

// ============================================================
// CMiniportWaveRTStream - Stream implementation
// ============================================================
class CMiniportWaveRTStream : public IMiniportWaveRTStream, public CUnknown
{
private:
    PPORTWAVERTSTREAM m_PortStream;
    PMDL              m_BufferMdl;
    PVOID             m_BufferVA;
    ULONG             m_BufferSize;
    KSSTATE           m_State;
    ULONG             m_Position;
    KTIMER            m_Timer;
    KDPC              m_Dpc;
    ULONG             m_FrameSize;

    static void NTAPI TimerDpc(PKDPC Dpc, PVOID Context, PVOID Arg1, PVOID Arg2);
    void        CopyFromSharedMemory();

public:
    DECLARE_STD_UNKNOWN();

    CMiniportWaveRTStream(PUNKNOWN pUnknown) : CUnknown(pUnknown)
    {
        m_PortStream = NULL;
        m_BufferMdl  = NULL;
        m_BufferVA   = NULL;
        m_BufferSize = 0;
        m_State      = KSSTATE_STOP;
        m_Position   = 0;
        m_FrameSize  = SOUNDBOX_CHANNELS * (SOUNDBOX_BITS_PER_SAMPLE / 8);
    }

    ~CMiniportWaveRTStream();

    IMP_IMiniportWaveRTStream;

    NTSTATUS Init(PPORTWAVERTSTREAM PortStream);
};

// ============================================================
// CMiniportWaveRT implementation
// ============================================================
CMiniportWaveRT::~CMiniportWaveRT()
{
    if (m_Port) m_Port->Release();
}

STDMETHODIMP_(NTSTATUS) CMiniportWaveRT::GetDescription(
    OUT PPCFILTER_DESCRIPTOR* Description)
{
    PAGED_CODE();
    *Description = &g_FilterDescriptor;
    return STATUS_SUCCESS;
}

STDMETHODIMP_(NTSTATUS) CMiniportWaveRT::Init(
    IN PUNKNOWN       UnknownAdapter,
    IN PRESOURCELIST   ResourceList,
    IN PPORTWAVERT     Port)
{
    PAGED_CODE();
    m_Port = Port;
    m_Port->AddRef();
    return STATUS_SUCCESS;
}

STDMETHODIMP_(NTSTATUS) CMiniportWaveRT::NewStream(
    OUT PMINIPORTWAVERTSTREAM* Stream,
    IN  PPORTWAVERTSTREAM      PortStream,
    IN  ULONG                  Pin,
    IN  BOOLEAN                Capture,
    IN  PKSDATAFORMAT          DataFormat)
{
    PAGED_CODE();
    NTSTATUS status;

    CMiniportWaveRTStream* pStream = new (NonPagedPoolNx, 'SBVD')
        CMiniportWaveRTStream(NULL);
    if (!pStream)
        return STATUS_INSUFFICIENT_RESOURCES;

    pStream->AddRef();
    status = pStream->Init(PortStream);
    if (NT_SUCCESS(status))
    {
        *Stream = (PMINIPORTWAVERTSTREAM)pStream;
    }
    else
    {
        pStream->Release();
    }

    return status;
}

STDMETHODIMP_(NTSTATUS) CMiniportWaveRT::GetDeviceDescription(
    OUT PDEVICE_DESCRIPTION DeviceDescription)
{
    PAGED_CODE();
    RtlZeroMemory(DeviceDescription, sizeof(DEVICE_DESCRIPTION));
    DeviceDescription->Version = DEVICE_DESCRIPTION_VERSION;
    DeviceDescription->Master = TRUE;
    DeviceDescription->ScatterGather = TRUE;
    DeviceDescription->Dma32BitAddresses = TRUE;
    DeviceDescription->MaximumLength = 0xFFFFFFFF;
    return STATUS_SUCCESS;
}

// IUnknown for CMiniportWaveRT
NTSTATUS CMiniportWaveRT::NonDelegatingQueryInterface(
    REFIID iid, PVOID* ppv)
{
    PAGED_CODE();
    if (IsEqualGUIDAligned(iid, IID_IUnknown))
        *ppv = (PUNKNOWN)(IMiniportWaveRT*)this;
    else if (IsEqualGUIDAligned(iid, IID_IMiniport))
        *ppv = (PMINIPORT)(IMiniportWaveRT*)this;
    else if (IsEqualGUIDAligned(iid, IID_IMiniportWaveRT))
        *ppv = (PMINIPORTWAVERT)this;
    else
    {
        *ppv = NULL;
        return STATUS_INVALID_PARAMETER;
    }

    ((PUNKNOWN)*ppv)->AddRef();
    return STATUS_SUCCESS;
}

// ============================================================
// CMiniportWaveRTStream implementation
// ============================================================
CMiniportWaveRTStream::~CMiniportWaveRTStream()
{
    KeCancelTimer(&m_Timer);
    if (m_PortStream) m_PortStream->Release();
}

NTSTATUS CMiniportWaveRTStream::Init(PPORTWAVERTSTREAM PortStream)
{
    m_PortStream = PortStream;
    m_PortStream->AddRef();

    KeInitializeTimer(&m_Timer);
    KeInitializeDpc(&m_Dpc, TimerDpc, this);

    return STATUS_SUCCESS;
}

STDMETHODIMP_(NTSTATUS) CMiniportWaveRTStream::AllocateAudioBuffer(
    IN  ULONG  RequestedSize,
    OUT PMDL*  AudioBufferMdl,
    OUT ULONG* ActualSize,
    OUT ULONG* OffsetFromFirstPage,
    OUT MEMORY_CACHING_TYPE* CacheType)
{
    PAGED_CODE();

    m_BufferSize = RequestedSize;
    m_BufferVA = ExAllocatePool2(POOL_FLAG_NON_PAGED, m_BufferSize, 'SBVD');
    if (!m_BufferVA)
        return STATUS_INSUFFICIENT_RESOURCES;

    RtlZeroMemory(m_BufferVA, m_BufferSize);

    m_BufferMdl = IoAllocateMdl(m_BufferVA, m_BufferSize, FALSE, FALSE, NULL);
    if (!m_BufferMdl)
    {
        ExFreePoolWithTag(m_BufferVA, 'SBVD');
        m_BufferVA = NULL;
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    MmBuildMdlForNonPagedPool(m_BufferMdl);

    *AudioBufferMdl = m_BufferMdl;
    *ActualSize = m_BufferSize;
    *OffsetFromFirstPage = 0;
    *CacheType = MmNonCached;

    return STATUS_SUCCESS;
}

STDMETHODIMP_(void) CMiniportWaveRTStream::FreeAudioBuffer(
    IN PMDL  AudioBufferMdl,
    IN ULONG BufferSize)
{
    PAGED_CODE();

    if (m_BufferMdl)
    {
        IoFreeMdl(m_BufferMdl);
        m_BufferMdl = NULL;
    }
    if (m_BufferVA)
    {
        ExFreePoolWithTag(m_BufferVA, 'SBVD');
        m_BufferVA = NULL;
    }
    m_BufferSize = 0;
}

STDMETHODIMP_(NTSTATUS) CMiniportWaveRTStream::GetHWLatency(
    OUT PKSRTAUDIO_HWLATENCY HwLatency)
{
    PAGED_CODE();
    HwLatency->FifoSize = 0;
    HwLatency->ChipsetDelay = 0;
    HwLatency->CodecDelay = 0;
    return STATUS_SUCCESS;
}

STDMETHODIMP_(NTSTATUS) CMiniportWaveRTStream::GetPosition(
    OUT PKSAUDIO_POSITION Position)
{
    Position->PlayOffset = m_Position;
    Position->WriteOffset = m_Position;
    return STATUS_SUCCESS;
}

STDMETHODIMP_(NTSTATUS) CMiniportWaveRTStream::SetState(
    IN KSSTATE State)
{
    PAGED_CODE();

    if (State == KSSTATE_RUN && m_State != KSSTATE_RUN)
    {
        // Start the timer - fires every 10ms to copy data
        LARGE_INTEGER dueTime;
        dueTime.QuadPart = -100000LL; // 10ms in 100ns units
        KeSetTimerEx(&m_Timer, dueTime, 10, &m_Dpc);
    }
    else if (State != KSSTATE_RUN && m_State == KSSTATE_RUN)
    {
        KeCancelTimer(&m_Timer);
    }

    m_State = State;
    return STATUS_SUCCESS;
}

STDMETHODIMP_(NTSTATUS) CMiniportWaveRTStream::GetClockRegister(
    OUT PKSRTAUDIO_HWREGISTER Register)
{
    return STATUS_NOT_IMPLEMENTED;
}

STDMETHODIMP_(NTSTATUS) CMiniportWaveRTStream::GetPositionRegister(
    OUT PKSRTAUDIO_HWREGISTER Register)
{
    return STATUS_NOT_IMPLEMENTED;
}

// Timer DPC - copies data from shared memory to WaveRT buffer
void NTAPI CMiniportWaveRTStream::TimerDpc(
    PKDPC Dpc, PVOID Context, PVOID Arg1, PVOID Arg2)
{
    CMiniportWaveRTStream* self = (CMiniportWaveRTStream*)Context;
    if (self->m_State == KSSTATE_RUN)
        self->CopyFromSharedMemory();
}

// Copy audio data from shared memory ring buffer to WaveRT buffer
void CMiniportWaveRTStream::CopyFromSharedMemory()
{
    if (!g_SharedBuffer || !m_BufferVA || m_BufferSize == 0)
        return;

    SOUNDBOX_SHARED_BUFFER* shared = (SOUNDBOX_SHARED_BUFFER*)g_SharedBuffer;
    if (!shared->Active)
    {
        // No data - fill with silence
        ULONG bytesPerPeriod = (SOUNDBOX_SAMPLE_RATE / 100) *
            SOUNDBOX_CHANNELS * (SOUNDBOX_BITS_PER_SAMPLE / 8); // 10ms of samples
        ULONG writeEnd = m_Position + bytesPerPeriod;

        if (writeEnd <= m_BufferSize)
        {
            RtlZeroMemory((PUCHAR)m_BufferVA + m_Position, bytesPerPeriod);
        }
        else
        {
            ULONG firstPart = m_BufferSize - m_Position;
            RtlZeroMemory((PUCHAR)m_BufferVA + m_Position, firstPart);
            RtlZeroMemory(m_BufferVA, bytesPerPeriod - firstPart);
        }
        m_Position = writeEnd % m_BufferSize;
        return;
    }

    PUCHAR sharedData = (PUCHAR)g_SharedBuffer + SOUNDBOX_DATA_OFFSET;
    LONG readPos  = shared->ReadPosition;
    LONG writePos = shared->WritePosition;
    LONG bufSize  = shared->BufferSize;

    // Calculate available data
    LONG available = writePos - readPos;
    if (available < 0) available += bufSize;

    // Copy up to 10ms of data per DPC
    ULONG bytesPerPeriod = (SOUNDBOX_SAMPLE_RATE / 100) *
        SOUNDBOX_CHANNELS * (SOUNDBOX_BITS_PER_SAMPLE / 8);
    ULONG toCopy = min((ULONG)available, bytesPerPeriod);

    if (toCopy == 0) return;

    // Copy to WaveRT buffer (handle wrap-around for both buffers)
    for (ULONG i = 0; i < toCopy; i++)
    {
        ((PUCHAR)m_BufferVA)[m_Position] = sharedData[readPos];
        m_Position = (m_Position + 1) % m_BufferSize;
        readPos = (readPos + 1) % bufSize;
    }

    // Update read position
    InterlockedExchange(&shared->ReadPosition, readPos);
}

// IUnknown for CMiniportWaveRTStream
NTSTATUS CMiniportWaveRTStream::NonDelegatingQueryInterface(
    REFIID iid, PVOID* ppv)
{
    PAGED_CODE();
    if (IsEqualGUIDAligned(iid, IID_IUnknown))
        *ppv = (PUNKNOWN)(IMiniportWaveRTStream*)this;
    else if (IsEqualGUIDAligned(iid, IID_IMiniportWaveRTStream))
        *ppv = (PMINIPORTWAVERTSTREAM)this;
    else
    {
        *ppv = NULL;
        return STATUS_INVALID_PARAMETER;
    }
    ((PUNKNOWN)*ppv)->AddRef();
    return STATUS_SUCCESS;
}

// ============================================================
// Factory function
// ============================================================
NTSTATUS CreateSoundBoxMiniport(PUNKNOWN* OutMiniport)
{
    CMiniportWaveRT* pMiniport = new (NonPagedPoolNx, 'SBVD')
        CMiniportWaveRT(NULL);
    if (!pMiniport)
        return STATUS_INSUFFICIENT_RESOURCES;

    pMiniport->AddRef();
    *OutMiniport = (PUNKNOWN)(IMiniportWaveRT*)pMiniport;
    return STATUS_SUCCESS;
}
