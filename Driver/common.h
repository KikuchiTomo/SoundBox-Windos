#pragma once

// Shared definitions between kernel driver and user-mode application
// GUID for device interface: {B5F6E3A1-7C4D-4E2F-9A1B-3D8E5F0C2A47}
// C# side: new Guid("B5F6E3A1-7C4D-4E2F-9A1B-3D8E5F0C2A47")

#define SOUNDBOX_DEVICE_INTERFACE \
    {0xB5F6E3A1, 0x7C4D, 0x4E2F, {0x9A, 0x1B, 0x3D, 0x8E, 0x5F, 0x0C, 0x2A, 0x47}}

// Shared memory section name (user-mode accessible)
#define SOUNDBOX_SECTION_NAME   L"Global\\SoundBoxAudioBuffer"
#define SOUNDBOX_EVENT_NAME     L"Global\\SoundBoxDataReady"

// Audio format
#define SOUNDBOX_SAMPLE_RATE    48000
#define SOUNDBOX_CHANNELS       2
#define SOUNDBOX_BITS_PER_SAMPLE 16
#define SOUNDBOX_RING_BUFFER_SIZE (SOUNDBOX_SAMPLE_RATE * SOUNDBOX_CHANNELS * 2 * 2) // ~2 sec buffer in bytes

// Shared memory layout
#pragma pack(push, 1)
typedef struct _SOUNDBOX_SHARED_BUFFER {
    volatile long WritePosition;    // Updated by user-mode app
    volatile long ReadPosition;     // Updated by driver
    long          BufferSize;       // Ring buffer size in bytes
    long          SampleRate;
    long          Channels;
    long          BitsPerSample;
    volatile long Active;           // 1 = streaming active
    char          Reserved[228];    // Pad to 256 bytes
    // Audio data ring buffer starts at offset 256
} SOUNDBOX_SHARED_BUFFER;
#pragma pack(pop)

#define SOUNDBOX_DATA_OFFSET    256
