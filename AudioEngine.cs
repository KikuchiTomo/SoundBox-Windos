using NAudio.CoreAudioApi;
using NAudio.Wave;
using SoundBox.Audio;

namespace SoundBox
{
    public class AudioEngine : IDisposable
    {
        private WasapiCapture? _capture;
        private readonly Dictionary<string, (WasapiOut output, BufferedWaveProvider buffer)> _outputDevices = new();
        private bool _running;
        private int _sampleRate;
        private int _channels;
        private string? _defaultOutputDeviceId;

        // Audio graph for node-based processing
        private AudioGraph? _graph;

        public bool IsRunning => _running;
        public event Action<float>? LevelUpdated;
        public event Action<float[]>? WaveformUpdated;

        public void SetGraph(AudioGraph graph)
        {
            _graph = graph;
        }

        public static List<(string Id, string Name)> GetInputDevices()
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            return devices.Select(d => (d.ID, d.FriendlyName)).ToList();
        }

        public static List<(string Id, string Name)> GetOutputDevices()
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            return devices.Select(d => (d.ID, d.FriendlyName)).ToList();
        }

        public void Start(string? inputDeviceId, string? defaultOutputDeviceId)
        {
            if (_running) Stop();
            _defaultOutputDeviceId = defaultOutputDeviceId;

            var enumerator = new MMDeviceEnumerator();

            // Input device
            MMDevice inputDevice;
            if (inputDeviceId != null)
                inputDevice = enumerator.GetDevice(inputDeviceId);
            else
                inputDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

            _capture = new WasapiCapture(inputDevice);
            _sampleRate = _capture.WaveFormat.SampleRate;
            _channels = _capture.WaveFormat.Channels;

            // Set graph sample rate
            if (_graph != null)
                _graph.SampleRate = _sampleRate;

            // Open output devices for each OutputNode
            OpenOutputDevices(enumerator);

            _capture.DataAvailable += OnDataAvailable;
            _capture.StartRecording();
            _running = true;
        }

        private void OpenOutputDevices(MMDeviceEnumerator enumerator)
        {
            if (_graph == null) return;

            var outputFormat = new WaveFormat(_sampleRate, 16, _channels);
            var openedIds = new HashSet<string>();

            foreach (var outNode in _graph.GetOutputNodes())
            {
                string devId = string.IsNullOrEmpty(outNode.DeviceId)
                    ? (_defaultOutputDeviceId ?? "")
                    : outNode.DeviceId;

                if (string.IsNullOrEmpty(devId) || openedIds.Contains(devId)) continue;
                openedIds.Add(devId);

                try
                {
                    var device = enumerator.GetDevice(devId);
                    var buf = new BufferedWaveProvider(outputFormat)
                    {
                        BufferDuration = TimeSpan.FromSeconds(2),
                        DiscardOnBufferOverflow = true
                    };
                    var wasapiOut = new WasapiOut(device, AudioClientShareMode.Shared, false, 50);
                    wasapiOut.Init(buf);
                    wasapiOut.Play();
                    _outputDevices[devId] = (wasapiOut, buf);
                }
                catch { }
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;

            var waveFormat = _capture!.WaveFormat;
            int bytesPerSample = waveFormat.BitsPerSample / 8;
            int sampleCount = e.BytesRecorded / bytesPerSample;

            // Convert to float
            float[] floatBuffer = new float[sampleCount];
            if (waveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                Buffer.BlockCopy(e.Buffer, 0, floatBuffer, 0, e.BytesRecorded);
            }
            else
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    short sample = BitConverter.ToInt16(e.Buffer, i * 2);
                    floatBuffer[i] = sample / 32768f;
                }
            }

            // Process through audio graph
            if (_graph != null)
            {
                _graph.Process(floatBuffer, sampleCount, _channels);

                // Route each OutputNode to its device
                float peak = 0;
                float[]? firstOutput = null;

                foreach (var outNode in _graph.GetOutputNodes())
                {
                    string devId = string.IsNullOrEmpty(outNode.DeviceId)
                        ? (_defaultOutputDeviceId ?? "")
                        : outNode.DeviceId;

                    float[] outBuf = new float[sampleCount];
                    outNode.GetOutputBuffer(outBuf, sampleCount, _channels);

                    if (firstOutput == null) firstOutput = outBuf;

                    // Peak level
                    for (int i = 0; i < sampleCount; i++)
                    {
                        float a = MathF.Abs(outBuf[i]);
                        if (a > peak) peak = a;
                    }

                    // Write to device buffer
                    if (_outputDevices.TryGetValue(devId, out var dev))
                    {
                        byte[] pcmOutput = new byte[sampleCount * 2];
                        for (int i = 0; i < sampleCount; i++)
                        {
                            short s = (short)(Math.Clamp(outBuf[i], -1.0f, 1.0f) * 32767);
                            BitConverter.GetBytes(s).CopyTo(pcmOutput, i * 2);
                        }
                        dev.buffer.AddSamples(pcmOutput, 0, pcmOutput.Length);
                    }
                }

                LevelUpdated?.Invoke(peak);
                if (firstOutput != null)
                    WaveformUpdated?.Invoke(firstOutput);
            }
            else
            {
                LevelUpdated?.Invoke(0);
            }
        }

        public void Stop()
        {
            _running = false;
            _capture?.StopRecording();

            foreach (var (_, (output, _)) in _outputDevices)
            {
                try { output.Stop(); } catch { }
                try { output.Dispose(); } catch { }
            }
            _outputDevices.Clear();

            _capture?.Dispose();
            _capture = null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
