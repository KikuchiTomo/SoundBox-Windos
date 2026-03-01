using System.Runtime.InteropServices;

namespace SoundBox.Audio
{
    // ================================================================
    // INPUT NODE - Receives mic/audio input (Mono + Stereo L/R)
    // ================================================================
    public class InputNode : AudioNode
    {
        private readonly Port _monoOut, _leftOut, _rightOut;

        public InputNode()
        {
            TypeName = "Input";
            Name = "Input";
            AccentColor = Color.FromArgb(0, 212, 170);
            _monoOut = AddOutput("Mono");
            _leftOut = AddOutput("Left");
            _rightOut = AddOutput("Right");
        }

        public void SetInputBuffer(float[] interleavedBuffer, int totalSamples, int channels)
        {
            int frames = totalSamples / Math.Max(1, channels);
            _monoOut.EnsureBuffer(frames);
            _leftOut.EnsureBuffer(frames);
            _rightOut.EnsureBuffer(frames);

            if (channels >= 2)
            {
                for (int i = 0; i < frames; i++)
                {
                    float l = interleavedBuffer[i * channels];
                    float r = interleavedBuffer[i * channels + 1];
                    _leftOut.Buffer[i] = l;
                    _rightOut.Buffer[i] = r;
                    _monoOut.Buffer[i] = (l + r) * 0.5f;
                }
            }
            else
            {
                for (int i = 0; i < frames; i++)
                {
                    float s = interleavedBuffer[i];
                    _monoOut.Buffer[i] = s;
                    _leftOut.Buffer[i] = s;
                    _rightOut.Buffer[i] = s;
                }
            }
        }

        public override void Process(int blockSize, int sampleRate) { }
    }

    // ================================================================
    // OUTPUT NODE - Sends processed audio (Mono + Stereo L/R)
    // ================================================================
    public class OutputNode : AudioNode
    {
        private readonly Port _monoIn, _leftIn, _rightIn;
        private string _deviceId = "";
        private string _deviceName = "";

        public string DeviceId
        {
            get => _deviceId;
            set => _deviceId = value ?? "";
        }

        public string DeviceName
        {
            get => _deviceName;
            set
            {
                _deviceName = value ?? "";
                Name = string.IsNullOrEmpty(_deviceName) ? "Output" : _deviceName;
            }
        }

        public OutputNode()
        {
            TypeName = "Output";
            Name = "Output";
            AccentColor = Color.FromArgb(0, 212, 170);
            _monoIn = AddInput("Mono");
            _leftIn = AddInput("Left");
            _rightIn = AddInput("Right");
        }

        public void GetOutputBuffer(float[] interleavedBuffer, int totalSamples, int channels)
        {
            int frames = totalSamples / Math.Max(1, channels);
            bool hasMono = _monoIn.Source != null;
            bool hasLeft = _leftIn.Source != null;
            bool hasRight = _rightIn.Source != null;

            if (channels >= 2)
            {
                for (int i = 0; i < frames; i++)
                {
                    float l, r;
                    if (hasLeft || hasRight)
                    {
                        l = hasLeft ? _leftIn.Read(i) : (hasMono ? _monoIn.Read(i) : 0f);
                        r = hasRight ? _rightIn.Read(i) : (hasMono ? _monoIn.Read(i) : 0f);
                    }
                    else
                    {
                        l = r = _monoIn.Read(i);
                    }
                    interleavedBuffer[i * channels] = l;
                    interleavedBuffer[i * channels + 1] = r;
                }
            }
            else
            {
                for (int i = 0; i < frames; i++)
                    interleavedBuffer[i] = _monoIn.Read(i);
            }
        }

        public override void Process(int blockSize, int sampleRate) { }
    }

    // ================================================================
    // GAIN NODE
    // ================================================================
    public class GainNode : AudioNode
    {
        private readonly Port _audioIn;
        private readonly Port _level;
        private readonly Port _audioOut;

        public GainNode()
        {
            TypeName = "Gain";
            Name = "Gain";
            AccentColor = Color.FromArgb(0, 212, 170);
            _audioIn = AddInput("Audio In");
            _level = AddParameter("Level", 0f, 3f, 1f, "x");
            _audioOut = AddOutput("Audio Out");
        }

        public override void Process(int blockSize, int sampleRate)
        {
            for (int i = 0; i < blockSize; i++)
            {
                float gain = _level.Modulated(i);
                _audioOut.Buffer[i] = Math.Clamp(_audioIn.Read(i) * gain, -1f, 1f);
            }
        }
    }

    // ================================================================
    // NOISE GATE NODE
    // ================================================================
    public class NoiseGateNode : AudioNode
    {
        private readonly Port _audioIn, _audioOut;
        private readonly Port _threshold, _hold;
        private bool _gateOpen;
        private int _holdCounter;

        public NoiseGateNode()
        {
            TypeName = "NoiseGate";
            Name = "Noise Gate";
            AccentColor = Color.FromArgb(230, 190, 40);
            _audioIn = AddInput("Audio In");
            _threshold = AddParameter("Threshold", 0f, 0.2f, 0.01f);
            _hold = AddParameter("Hold", 10f, 500f, 100f, "ms");
            _audioOut = AddOutput("Audio Out");
        }

        public override void Process(int blockSize, int sampleRate)
        {
            int holdSamples = (int)(_hold.BaseValue * sampleRate / 1000f);
            float thresh = _threshold.Modulated(0);

            for (int i = 0; i < blockSize; i++)
            {
                float s = _audioIn.Read(i);
                if (MathF.Abs(s) > thresh) { _gateOpen = true; _holdCounter = holdSamples; }
                else if (_holdCounter > 0) _holdCounter--;
                else _gateOpen = false;
                _audioOut.Buffer[i] = _gateOpen ? s : 0f;
            }
        }
    }

    // ================================================================
    // REVERB NODE (Schroeder: 4 comb + 2 allpass)
    // ================================================================
    public class ReverbNode : AudioNode
    {
        private readonly Port _audioIn, _audioOut;
        private readonly Port _time, _mix, _damping;

        private float[][] _combBuf = Array.Empty<float[]>();
        private int[] _combPos = Array.Empty<int>();
        private float[] _combFilter = Array.Empty<float>();
        private float[][] _apBuf = Array.Empty<float[]>();
        private int[] _apPos = Array.Empty<int>();

        private static readonly int[] CombDelays = { 1557, 1617, 1491, 1422 };
        private static readonly int[] ApDelays = { 225, 556 };

        public ReverbNode()
        {
            TypeName = "Reverb";
            Name = "Reverb";
            AccentColor = Color.FromArgb(255, 140, 50);
            _audioIn = AddInput("Audio In");
            _time = AddParameter("Time", 0.1f, 5f, 1.5f, "s");
            _mix = AddParameter("Mix", 0f, 1f, 0.3f);
            _damping = AddParameter("Damp", 0f, 1f, 0.3f);
            _audioOut = AddOutput("Audio Out");
        }

        public override void Initialize(int sampleRate)
        {
            float scale = sampleRate / 48000f;
            _combBuf = new float[4][];
            _combPos = new int[4];
            _combFilter = new float[4];
            for (int i = 0; i < 4; i++)
            {
                int sz = Math.Max(1, (int)(CombDelays[i] * scale));
                _combBuf[i] = new float[sz];
                _combPos[i] = 0;
                _combFilter[i] = 0;
            }
            _apBuf = new float[2][];
            _apPos = new int[2];
            for (int i = 0; i < 2; i++)
            {
                int sz = Math.Max(1, (int)(ApDelays[i] * scale));
                _apBuf[i] = new float[sz];
                _apPos[i] = 0;
            }
        }

        public override void Process(int blockSize, int sampleRate)
        {
            if (_combBuf.Length == 0) Initialize(sampleRate);
            float rt60 = _time.Modulated(0);
            float wet = _mix.Modulated(0);
            float damp = _damping.Modulated(0);
            float dampInv = 1f - damp;

            for (int i = 0; i < blockSize; i++)
            {
                float input = _audioIn.Read(i);
                float combSum = 0;

                for (int c = 0; c < 4; c++)
                {
                    float fb = MathF.Pow(10f, -3f * _combBuf[c].Length / (rt60 * sampleRate));
                    float delayed = _combBuf[c][_combPos[c]];
                    _combFilter[c] = delayed * dampInv + _combFilter[c] * damp;
                    _combBuf[c][_combPos[c]] = input + _combFilter[c] * fb;
                    _combPos[c] = (_combPos[c] + 1) % _combBuf[c].Length;
                    combSum += delayed;
                }

                float o = combSum * 0.25f;
                for (int a = 0; a < 2; a++)
                {
                    float delayed = _apBuf[a][_apPos[a]];
                    float v = o + delayed * -0.5f;
                    _apBuf[a][_apPos[a]] = o + delayed * 0.5f;
                    o = v;
                    _apPos[a] = (_apPos[a] + 1) % _apBuf[a].Length;
                }

                _audioOut.Buffer[i] = Math.Clamp(input * (1f - wet) + o * wet, -1f, 1f);
            }
        }
    }

    // ================================================================
    // COMPRESSOR NODE
    // ================================================================
    public class CompressorNode : AudioNode
    {
        private readonly Port _audioIn, _audioOut, _envOut;
        private readonly Port _threshold, _ratio, _attack, _release;
        private float _envelope;

        public CompressorNode()
        {
            TypeName = "Compressor";
            Name = "Compressor";
            AccentColor = Color.FromArgb(160, 100, 240);
            _audioIn = AddInput("Audio In");
            _threshold = AddParameter("Thresh", 0.01f, 1f, 0.5f);
            _ratio = AddParameter("Ratio", 1f, 20f, 4f, "x");
            _attack = AddParameter("Attack", 0.1f, 100f, 5f, "ms");
            _release = AddParameter("Release", 1f, 500f, 50f, "ms");
            _audioOut = AddOutput("Audio Out");
            _envOut = AddOutput("Envelope", PortKind.Control);
        }

        public override void Process(int blockSize, int sampleRate)
        {
            float atkCoeff = MathF.Exp(-1f / (_attack.BaseValue * 0.001f * sampleRate));
            float relCoeff = MathF.Exp(-1f / (_release.BaseValue * 0.001f * sampleRate));
            float thresh = _threshold.Modulated(0);
            float ratio = _ratio.Modulated(0);

            for (int i = 0; i < blockSize; i++)
            {
                float s = _audioIn.Read(i);
                float abs = MathF.Abs(s);

                _envelope = abs > _envelope
                    ? atkCoeff * _envelope + (1f - atkCoeff) * abs
                    : relCoeff * _envelope + (1f - relCoeff) * abs;

                if (_envelope > thresh)
                {
                    float dbOver = 20f * MathF.Log10(_envelope / thresh);
                    float dbRed = dbOver * (1f - 1f / ratio);
                    float gainRed = MathF.Pow(10f, -dbRed / 20f);
                    s *= gainRed;
                }

                _audioOut.Buffer[i] = s;
                _envOut.Buffer[i] = _envelope;
            }
        }
    }

    // ================================================================
    // AUTO-TUNE NODE (uses C++ FFTW-based pitch correction)
    // ================================================================
    public class AutoTuneNode : AudioNode
    {
        private readonly Port _audioIn, _audioOut, _pitchOut;
        private readonly Port _key, _scale, _speed, _amount;
        private IntPtr _handle;

        public AutoTuneNode()
        {
            TypeName = "AutoTune";
            Name = "Auto-Tune";
            AccentColor = Color.FromArgb(240, 80, 160);
            _audioIn = AddInput("Audio In");
            _key = AddParameter("Key", 0, 11, 0,
                new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" });
            _scale = AddParameter("Scale", 0, 3, 0,
                new[] { "Chromatic", "Major", "Minor", "Pentatonic" });
            _speed = AddParameter("Speed", 0, 1, 0);
            _amount = AddParameter("Amount", 0, 1, 1);
            _audioOut = AddOutput("Audio Out");
            _pitchOut = AddOutput("Pitch", PortKind.Control);
        }

        public override void Initialize(int sampleRate)
        {
            try { _handle = NativeDSP.SB_CreateAutoTune(sampleRate); } catch { }
        }

        public override void Process(int blockSize, int sampleRate)
        {
            if (_handle == IntPtr.Zero)
            {
                for (int i = 0; i < blockSize; i++)
                    _audioOut.Buffer[i] = _audioIn.Read(i);
                return;
            }

            try
            {
                NativeDSP.SB_SetAutoTuneParamI(_handle, 0, 1); // enabled
                NativeDSP.SB_SetAutoTuneParamI(_handle, 1, (int)_key.BaseValue);
                int[] scaleValues = { 0xFFF, 0xAB5, 0x5AD, 0x295 };
                int si = Math.Clamp((int)_scale.BaseValue, 0, 3);
                NativeDSP.SB_SetAutoTuneParamI(_handle, 2, scaleValues[si]);
                NativeDSP.SB_SetAutoTuneParamF(_handle, 3, _speed.Modulated(0));
                NativeDSP.SB_SetAutoTuneParamF(_handle, 4, _amount.Modulated(0));

                // Copy input to temp buffer for in-place processing
                float[] temp = new float[blockSize];
                for (int i = 0; i < blockSize; i++) temp[i] = _audioIn.Read(i);

                NativeDSP.SB_ProcessAutoTune(_handle, temp, blockSize);

                Array.Copy(temp, _audioOut.Buffer, blockSize);

                float pitch = NativeDSP.SB_GetAutoTuneInfoF(_handle, 0);
                for (int i = 0; i < blockSize; i++)
                    _pitchOut.Buffer[i] = pitch / 1000f; // Normalize to 0~1 range roughly
            }
            catch
            {
                for (int i = 0; i < blockSize; i++)
                    _audioOut.Buffer[i] = _audioIn.Read(i);
            }
        }

        public override void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                try { NativeDSP.SB_DestroyAutoTune(_handle); } catch { }
                _handle = IntPtr.Zero;
            }
        }
    }

    // ================================================================
    // PITCH SHIFT NODE (manual pitch shift via phase vocoder)
    // ================================================================
    public class PitchShiftNode : AudioNode
    {
        private readonly Port _audioIn, _audioOut;
        private readonly Port _semitones;
        private IntPtr _handle;

        public PitchShiftNode()
        {
            TypeName = "PitchShift";
            Name = "Pitch Shift";
            AccentColor = Color.FromArgb(240, 80, 160);
            _audioIn = AddInput("Audio In");
            _semitones = AddParameter("Shift", -12f, 12f, 0f, "st");
            _audioOut = AddOutput("Audio Out");
        }

        public override void Initialize(int sampleRate)
        {
            try { _handle = NativeDSP.SB_CreatePitchShift(sampleRate); } catch { }
        }

        public override void Process(int blockSize, int sampleRate)
        {
            float shift = _semitones.Modulated(0);

            if (_handle == IntPtr.Zero || MathF.Abs(shift) < 0.01f)
            {
                for (int i = 0; i < blockSize; i++)
                    _audioOut.Buffer[i] = _audioIn.Read(i);
                return;
            }

            try
            {
                float[] temp = new float[blockSize];
                for (int i = 0; i < blockSize; i++) temp[i] = _audioIn.Read(i);
                NativeDSP.SB_ProcessPitchShift(_handle, temp, blockSize, shift);
                Array.Copy(temp, _audioOut.Buffer, blockSize);
            }
            catch
            {
                for (int i = 0; i < blockSize; i++)
                    _audioOut.Buffer[i] = _audioIn.Read(i);
            }
        }

        public override void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                try { NativeDSP.SB_DestroyPitchShift(_handle); } catch { }
                _handle = IntPtr.Zero;
            }
        }
    }

    // ================================================================
    // PAN NODE (stereo panning)
    // ================================================================
    public class PanNode : AudioNode
    {
        private readonly Port _audioIn, _audioOut;
        private readonly Port _pan;

        public PanNode()
        {
            TypeName = "Pan";
            Name = "Pan";
            AccentColor = Color.FromArgb(80, 180, 255);
            _audioIn = AddInput("Audio In");
            _pan = AddParameter("Pan", -1f, 1f, 0f);
            _audioOut = AddOutput("Audio Out");
        }

        public override void Process(int blockSize, int sampleRate)
        {
            for (int i = 0; i < blockSize; i++)
            {
                float s = _audioIn.Read(i);
                float p = _pan.Modulated(i);
                // Equal-power pan: L = cos(theta), R = sin(theta)
                float angle = (p + 1f) * 0.5f * MathF.PI * 0.5f;
                float l = MathF.Cos(angle) * s;
                float r = MathF.Sin(angle) * s;
                // Interleave or sum for mono output
                _audioOut.Buffer[i] = l + r; // Summed mono with pan emphasis
            }
        }
    }

    // ================================================================
    // WAH NODE (resonant bandpass filter with manual frequency)
    // ================================================================
    public class WahNode : AudioNode
    {
        private readonly Port _audioIn, _audioOut;
        private readonly Port _freq, _resonance;
        // Biquad state
        private float _x1, _x2, _y1, _y2;

        public WahNode()
        {
            TypeName = "Wah";
            Name = "Wah";
            AccentColor = Color.FromArgb(255, 100, 100);
            _audioIn = AddInput("Audio In");
            _freq = AddParameter("Freq", 200f, 3000f, 800f, "Hz");
            _resonance = AddParameter("Q", 0.5f, 15f, 5f);
            _audioOut = AddOutput("Audio Out");
        }

        public override void Process(int blockSize, int sampleRate)
        {
            float f = _freq.Modulated(0);
            float q = _resonance.Modulated(0);

            // Biquad bandpass coefficients
            float w0 = 2f * MathF.PI * f / sampleRate;
            float alpha = MathF.Sin(w0) / (2f * q);
            float a0 = 1f + alpha;
            float b0 = alpha / a0;
            float b1 = 0f;
            float b2 = -alpha / a0;
            float a1 = -2f * MathF.Cos(w0) / a0;
            float a2 = (1f - alpha) / a0;

            for (int i = 0; i < blockSize; i++)
            {
                float x = _audioIn.Read(i);
                float y = b0 * x + b1 * _x1 + b2 * _x2 - a1 * _y1 - a2 * _y2;
                _x2 = _x1; _x1 = x;
                _y2 = _y1; _y1 = y;
                _audioOut.Buffer[i] = Math.Clamp(y, -1f, 1f);
            }
        }
    }

    // ================================================================
    // AUTO-WAH NODE (envelope-following wah)
    // ================================================================
    public class AutoWahNode : AudioNode
    {
        private readonly Port _audioIn, _audioOut;
        private readonly Port _sensitivity, _minFreq, _maxFreq, _q;
        private float _envelope;
        private float _x1, _x2, _y1, _y2;

        public AutoWahNode()
        {
            TypeName = "AutoWah";
            Name = "Auto-Wah";
            AccentColor = Color.FromArgb(255, 100, 100);
            _audioIn = AddInput("Audio In");
            _sensitivity = AddParameter("Sens", 0f, 1f, 0.5f);
            _minFreq = AddParameter("Min Hz", 200f, 1000f, 300f, "Hz");
            _maxFreq = AddParameter("Max Hz", 1000f, 5000f, 2500f, "Hz");
            _q = AddParameter("Q", 0.5f, 15f, 5f);
            _audioOut = AddOutput("Audio Out");
        }

        public override void Process(int blockSize, int sampleRate)
        {
            float sens = _sensitivity.Modulated(0);
            float minF = _minFreq.Modulated(0);
            float maxF = _maxFreq.Modulated(0);
            float q = _q.Modulated(0);
            float atkCoeff = MathF.Exp(-1f / (0.005f * sampleRate));
            float relCoeff = MathF.Exp(-1f / (0.05f * sampleRate));

            for (int i = 0; i < blockSize; i++)
            {
                float x = _audioIn.Read(i);
                float abs = MathF.Abs(x);

                _envelope = abs > _envelope
                    ? atkCoeff * _envelope + (1f - atkCoeff) * abs
                    : relCoeff * _envelope + (1f - relCoeff) * abs;

                float freqControl = Math.Clamp(_envelope * sens * 10f, 0f, 1f);
                float f = minF + (maxF - minF) * freqControl;

                float w0 = 2f * MathF.PI * f / sampleRate;
                float alpha = MathF.Sin(w0) / (2f * q);
                float a0 = 1f + alpha;
                float b0 = alpha / a0;
                float b2 = -alpha / a0;
                float a1 = -2f * MathF.Cos(w0) / a0;
                float a2 = (1f - alpha) / a0;

                float y = b0 * x + 0f * _x1 + b2 * _x2 - a1 * _y1 - a2 * _y2;
                _x2 = _x1; _x1 = x;
                _y2 = _y1; _y1 = y;
                _audioOut.Buffer[i] = Math.Clamp(y, -1f, 1f);
            }
        }
    }

    // ================================================================
    // VOCODER NODE (channel vocoder with bandpass filterbank)
    // ================================================================
    public class VocoderNode : AudioNode
    {
        private readonly Port _modIn, _carrierIn, _audioOut;
        private readonly Port _bands, _release;

        private const int MaxBands = 32;
        // Biquad states for modulator and carrier filters
        private float[] _mx1 = new float[MaxBands], _mx2 = new float[MaxBands];
        private float[] _my1 = new float[MaxBands], _my2 = new float[MaxBands];
        private float[] _cx1 = new float[MaxBands], _cx2 = new float[MaxBands];
        private float[] _cy1 = new float[MaxBands], _cy2 = new float[MaxBands];
        private float[] _envs = new float[MaxBands];

        public VocoderNode()
        {
            TypeName = "Vocoder";
            Name = "Vocoder";
            AccentColor = Color.FromArgb(200, 60, 200);
            _modIn = AddInput("Modulator");
            _carrierIn = AddInput("Carrier");
            _bands = AddParameter("Bands", 8, 32, 16);
            _release = AddParameter("Release", 1f, 100f, 20f, "ms");
            _audioOut = AddOutput("Audio Out");
        }

        public override void Process(int blockSize, int sampleRate)
        {
            int numBands = (int)_bands.BaseValue;
            if (numBands < 4) numBands = 4;
            if (numBands > MaxBands) numBands = MaxBands;

            float relCoeff = MathF.Exp(-1f / (_release.BaseValue * 0.001f * sampleRate));

            for (int i = 0; i < blockSize; i++)
            {
                float mod = _modIn.Read(i);
                float carrier = _carrierIn.Read(i);
                float output = 0f;

                for (int b = 0; b < numBands; b++)
                {
                    // Logarithmic frequency distribution: 100Hz to 8000Hz
                    float frac = (float)b / numBands;
                    float freq = 100f * MathF.Pow(80f, frac);
                    float q = 4f;

                    float w0 = 2f * MathF.PI * freq / sampleRate;
                    float alpha = MathF.Sin(w0) / (2f * q);
                    float a0 = 1f + alpha;
                    float b0 = alpha / a0;
                    float b2 = -alpha / a0;
                    float a1 = -2f * MathF.Cos(w0) / a0;
                    float a2 = (1f - alpha) / a0;

                    // Filter modulator
                    float my = b0 * mod + b2 * _mx2[b] - a1 * _my1[b] - a2 * _my2[b];
                    _mx2[b] = _mx1[b]; _mx1[b] = mod;
                    _my2[b] = _my1[b]; _my1[b] = my;

                    // Envelope follower
                    float env = MathF.Abs(my);
                    _envs[b] = env > _envs[b] ? env : relCoeff * _envs[b] + (1f - relCoeff) * env;

                    // Filter carrier
                    float cy = b0 * carrier + b2 * _cx2[b] - a1 * _cy1[b] - a2 * _cy2[b];
                    _cx2[b] = _cx1[b]; _cx1[b] = carrier;
                    _cy2[b] = _cy1[b]; _cy1[b] = cy;

                    // Apply envelope to carrier band
                    output += cy * _envs[b] * 3f; // Boost factor
                }

                _audioOut.Buffer[i] = Math.Clamp(output, -1f, 1f);
            }
        }
    }

    // ================================================================
    // OSCILLATOR NODE (LFO / signal generator)
    // ================================================================
    public class OscillatorNode : AudioNode
    {
        private readonly Port _freq, _amplitude, _waveform;
        private readonly Port _audioOut, _controlOut;
        private double _phase;

        public OscillatorNode()
        {
            TypeName = "Oscillator";
            Name = "Oscillator";
            AccentColor = Color.FromArgb(100, 220, 100);
            _freq = AddParameter("Freq", 0.1f, 1000f, 2f, "Hz");
            _amplitude = AddParameter("Amp", 0f, 1f, 1f);
            _waveform = AddParameter("Wave", 0f, 3f, 0f,
                new[] { "Sine", "Saw", "Square", "Triangle" });
            _audioOut = AddOutput("Audio Out");
            _controlOut = AddOutput("Control", PortKind.Control);
        }

        public override void Process(int blockSize, int sampleRate)
        {
            float freq = _freq.Modulated(0);
            float amp = _amplitude.Modulated(0);
            int wave = (int)_waveform.BaseValue;
            double inc = freq / sampleRate;

            for (int i = 0; i < blockSize; i++)
            {
                float v = wave switch
                {
                    0 => MathF.Sin((float)(_phase * 2.0 * Math.PI)),                // sine
                    1 => (float)(2.0 * (_phase - Math.Floor(_phase + 0.5))),         // saw
                    2 => _phase < 0.5 ? 1f : -1f,                                   // square
                    3 => (float)(4.0 * Math.Abs(_phase - Math.Floor(_phase + 0.5)) - 1.0), // triangle
                    _ => 0f
                };

                v *= amp;
                _audioOut.Buffer[i] = v;
                _controlOut.Buffer[i] = v;

                _phase += inc;
                if (_phase >= 1.0) _phase -= 1.0;
            }
        }
    }

    // ================================================================
    // POWER DETECTOR NODE (RMS envelope follower)
    // ================================================================
    public class PowerDetectNode : AudioNode
    {
        private readonly Port _audioIn;
        private readonly Port _smoothing;
        private readonly Port _controlOut;
        private float _rms;

        public PowerDetectNode()
        {
            TypeName = "PowerDetect";
            Name = "Power Detect";
            AccentColor = Color.FromArgb(255, 200, 80);
            _audioIn = AddInput("Audio In");
            _smoothing = AddParameter("Smooth", 1f, 200f, 30f, "ms");
            _controlOut = AddOutput("Power", PortKind.Control);
        }

        public override void Process(int blockSize, int sampleRate)
        {
            float coeff = MathF.Exp(-1f / (_smoothing.BaseValue * 0.001f * sampleRate));

            for (int i = 0; i < blockSize; i++)
            {
                float s = _audioIn.Read(i);
                float sq = s * s;
                _rms = coeff * _rms + (1f - coeff) * sq;
                _controlOut.Buffer[i] = MathF.Sqrt(_rms);
            }
        }
    }

    // ================================================================
    // MIXER NODE (mix up to 4 inputs)
    // ================================================================
    public class MixerNode : AudioNode
    {
        private readonly Port _in1, _in2, _in3, _in4;
        private readonly Port _vol1, _vol2, _vol3, _vol4;
        private readonly Port _audioOut;

        public MixerNode()
        {
            TypeName = "Mixer";
            Name = "Mixer";
            AccentColor = Color.FromArgb(150, 150, 170);
            _in1 = AddInput("In 1");
            _in2 = AddInput("In 2");
            _in3 = AddInput("In 3");
            _in4 = AddInput("In 4");
            _vol1 = AddParameter("Vol 1", 0f, 2f, 1f);
            _vol2 = AddParameter("Vol 2", 0f, 2f, 1f);
            _vol3 = AddParameter("Vol 3", 0f, 2f, 1f);
            _vol4 = AddParameter("Vol 4", 0f, 2f, 1f);
            _audioOut = AddOutput("Audio Out");
        }

        public override void Process(int blockSize, int sampleRate)
        {
            for (int i = 0; i < blockSize; i++)
            {
                float sum = _in1.Read(i) * _vol1.Modulated(i)
                          + _in2.Read(i) * _vol2.Modulated(i)
                          + _in3.Read(i) * _vol3.Modulated(i)
                          + _in4.Read(i) * _vol4.Modulated(i);
                _audioOut.Buffer[i] = Math.Clamp(sum, -1f, 1f);
            }
        }
    }

    // ================================================================
    // MATH ADD NODE (A + B)
    // ================================================================
    public class MathAddNode : AudioNode
    {
        private readonly Port _inA, _inB, _audioOut;

        public MathAddNode()
        {
            TypeName = "MathAdd";
            Name = "Add";
            AccentColor = Color.FromArgb(120, 200, 230);
            _inA = AddInput("A");
            _inB = AddInput("B");
            _audioOut = AddOutput("Out");
        }

        public override void Process(int blockSize, int sampleRate)
        {
            for (int i = 0; i < blockSize; i++)
                _audioOut.Buffer[i] = Math.Clamp(_inA.Read(i) + _inB.Read(i), -1f, 1f);
        }
    }

    // ================================================================
    // MATH SUBTRACT NODE (A - B)
    // ================================================================
    public class MathSubNode : AudioNode
    {
        private readonly Port _inA, _inB, _audioOut;

        public MathSubNode()
        {
            TypeName = "MathSub";
            Name = "Subtract";
            AccentColor = Color.FromArgb(120, 200, 230);
            _inA = AddInput("A");
            _inB = AddInput("B");
            _audioOut = AddOutput("Out");
        }

        public override void Process(int blockSize, int sampleRate)
        {
            for (int i = 0; i < blockSize; i++)
                _audioOut.Buffer[i] = Math.Clamp(_inA.Read(i) - _inB.Read(i), -1f, 1f);
        }
    }

    // ================================================================
    // MATH MULTIPLY NODE (A * B)
    // ================================================================
    public class MathMulNode : AudioNode
    {
        private readonly Port _inA, _inB, _audioOut;

        public MathMulNode()
        {
            TypeName = "MathMul";
            Name = "Multiply";
            AccentColor = Color.FromArgb(120, 200, 230);
            _inA = AddInput("A");
            _inB = AddInput("B");
            _audioOut = AddOutput("Out");
        }

        public override void Process(int blockSize, int sampleRate)
        {
            for (int i = 0; i < blockSize; i++)
                _audioOut.Buffer[i] = _inA.Read(i) * _inB.Read(i);
        }
    }

    // ================================================================
    // CLIP NODE (clamp signal to range)
    // ================================================================
    public class ClipNode : AudioNode
    {
        private readonly Port _audioIn, _audioOut;
        private readonly Port _min, _max;

        public ClipNode()
        {
            TypeName = "Clip";
            Name = "Clip";
            AccentColor = Color.FromArgb(120, 200, 230);
            _audioIn = AddInput("In");
            _min = AddParameter("Min", -1f, 0f, -1f);
            _max = AddParameter("Max", 0f, 1f, 1f);
            _audioOut = AddOutput("Out");
        }

        public override void Process(int blockSize, int sampleRate)
        {
            float lo = _min.Modulated(0);
            float hi = _max.Modulated(0);
            for (int i = 0; i < blockSize; i++)
                _audioOut.Buffer[i] = Math.Clamp(_audioIn.Read(i), lo, hi);
        }
    }

    // ================================================================
    // SPLITTER NODE (1 input → 4 identical outputs)
    // ================================================================
    public class SplitterNode : AudioNode
    {
        private readonly Port _audioIn;
        private readonly Port _out1, _out2, _out3, _out4;

        public SplitterNode()
        {
            TypeName = "Splitter";
            Name = "Splitter";
            AccentColor = Color.FromArgb(150, 150, 170);
            _audioIn = AddInput("In");
            _out1 = AddOutput("Out 1");
            _out2 = AddOutput("Out 2");
            _out3 = AddOutput("Out 3");
            _out4 = AddOutput("Out 4");
        }

        public override void Process(int blockSize, int sampleRate)
        {
            for (int i = 0; i < blockSize; i++)
            {
                float s = _audioIn.Read(i);
                _out1.Buffer[i] = s;
                _out2.Buffer[i] = s;
                _out3.Buffer[i] = s;
                _out4.Buffer[i] = s;
            }
        }
    }

    // ================================================================
    // STEREO MERGE NODE (L + R → Stereo interleaved, or Mono)
    // ================================================================
    public class StereoMergeNode : AudioNode
    {
        private readonly Port _leftIn, _rightIn;
        private readonly Port _leftOut, _rightOut, _monoOut;

        public StereoMergeNode()
        {
            TypeName = "StereoMerge";
            Name = "Stereo Merge";
            AccentColor = Color.FromArgb(80, 180, 255);
            _leftIn = AddInput("Left");
            _rightIn = AddInput("Right");
            _leftOut = AddOutput("Left");
            _rightOut = AddOutput("Right");
            _monoOut = AddOutput("Mono");
        }

        public override void Process(int blockSize, int sampleRate)
        {
            for (int i = 0; i < blockSize; i++)
            {
                float l = _leftIn.Read(i);
                float r = _rightIn.Read(i);
                _leftOut.Buffer[i] = l;
                _rightOut.Buffer[i] = r;
                _monoOut.Buffer[i] = (l + r) * 0.5f;
            }
        }
    }

    // ================================================================
    // STEREO SPLIT NODE (Mono → L + R, or pass-through L/R)
    // ================================================================
    public class StereoSplitNode : AudioNode
    {
        private readonly Port _monoIn;
        private readonly Port _leftOut, _rightOut;

        public StereoSplitNode()
        {
            TypeName = "StereoSplit";
            Name = "Stereo Split";
            AccentColor = Color.FromArgb(80, 180, 255);
            _monoIn = AddInput("Mono");
            _leftOut = AddOutput("Left");
            _rightOut = AddOutput("Right");
        }

        public override void Process(int blockSize, int sampleRate)
        {
            for (int i = 0; i < blockSize; i++)
            {
                float s = _monoIn.Read(i);
                _leftOut.Buffer[i] = s;
                _rightOut.Buffer[i] = s;
            }
        }
    }

    // ================================================================
    // CONSTANT NODE (fixed value output)
    // ================================================================
    public class ConstantNode : AudioNode
    {
        private readonly Port _value;
        private readonly Port _audioOut, _controlOut;

        public ConstantNode()
        {
            TypeName = "Constant";
            Name = "Constant";
            AccentColor = Color.FromArgb(180, 180, 200);
            _value = AddParameter("Value", -100f, 100f, 1f);
            _audioOut = AddOutput("Audio");
            _controlOut = AddOutput("Control", PortKind.Control);
        }

        public override void Process(int blockSize, int sampleRate)
        {
            float v = _value.Modulated(0);
            for (int i = 0; i < blockSize; i++)
            {
                _audioOut.Buffer[i] = v;
                _controlOut.Buffer[i] = v;
            }
        }
    }
}
