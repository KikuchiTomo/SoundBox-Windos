using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

namespace SoundBox
{
    public class SharedAudioBuffer : IDisposable
    {
        private const string SectionName = "Global\\SoundBoxAudioBuffer";
        private const string EventName = "Global\\SoundBoxDataReady";
        private const int DataOffset = 256;
        private const int RingBufferSize = 48000 * 2 * 2 * 2; // ~2 sec

        private MemoryMappedFile? _mmf;
        private MemoryMappedViewAccessor? _accessor;
        private EventWaitHandle? _dataEvent;
        private bool _disposed;

        // Offsets matching SOUNDBOX_SHARED_BUFFER in common.h
        private const int OFF_WRITE_POS     = 0;
        private const int OFF_READ_POS      = 4;
        private const int OFF_BUFFER_SIZE   = 8;
        private const int OFF_SAMPLE_RATE   = 12;
        private const int OFF_CHANNELS      = 16;
        private const int OFF_BITS          = 20;
        private const int OFF_ACTIVE        = 24;

        public bool IsOpen => _mmf != null;

        public bool Open()
        {
            try
            {
                _mmf = MemoryMappedFile.OpenExisting(SectionName,
                    MemoryMappedFileRights.ReadWrite);
                _accessor = _mmf.CreateViewAccessor();
                _dataEvent = EventWaitHandle.OpenExisting(EventName);
                return true;
            }
            catch
            {
                Close();
                return false;
            }
        }

        public bool CreateLocal()
        {
            try
            {
                long totalSize = DataOffset + RingBufferSize;
                _mmf = MemoryMappedFile.CreateOrOpen(SectionName, totalSize,
                    MemoryMappedFileAccess.ReadWrite);
                _accessor = _mmf.CreateViewAccessor();

                // Initialize header
                _accessor.Write(OFF_BUFFER_SIZE, RingBufferSize);
                _accessor.Write(OFF_SAMPLE_RATE, 48000);
                _accessor.Write(OFF_CHANNELS, 2);
                _accessor.Write(OFF_BITS, 16);
                _accessor.Write(OFF_WRITE_POS, 0);
                _accessor.Write(OFF_READ_POS, 0);
                _accessor.Write(OFF_ACTIVE, 0);

                _dataEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
                return true;
            }
            catch
            {
                Close();
                return false;
            }
        }

        public void SetActive(bool active)
        {
            _accessor?.Write(OFF_ACTIVE, active ? 1 : 0);
        }

        public void WriteAudio(byte[] pcmData, int offset, int count)
        {
            if (_accessor == null) return;

            int writePos = _accessor.ReadInt32(OFF_WRITE_POS);
            int bufSize = _accessor.ReadInt32(OFF_BUFFER_SIZE);
            if (bufSize <= 0) return;

            for (int i = 0; i < count; i++)
            {
                _accessor.Write(DataOffset + writePos, pcmData[offset + i]);
                writePos = (writePos + 1) % bufSize;
            }

            _accessor.Write(OFF_WRITE_POS, writePos);
            _dataEvent?.Set();
        }

        public void Close()
        {
            _accessor?.Dispose();
            _accessor = null;
            _mmf?.Dispose();
            _mmf = null;
            _dataEvent?.Dispose();
            _dataEvent = null;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Close();
                _disposed = true;
            }
        }
    }
}
