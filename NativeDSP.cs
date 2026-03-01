using System.Runtime.InteropServices;

namespace SoundBox
{
    public static class NativeDSP
    {
        private const string DLL = "SoundBoxDSP";

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SB_Init(int sampleRate, int channels);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SB_Shutdown();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SB_Process(float[] buffer, int sampleCount);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SB_SetGainEnabled(int enabled);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SB_SetGain(float gain);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SB_SetNoiseGateEnabled(int enabled);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SB_SetNoiseGateThreshold(float threshold);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SB_SetReverbEnabled(int enabled);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SB_SetReverbParams(float reverbTime, float mix);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SB_SetCompressorEnabled(int enabled);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SB_SetCompressorParams(float threshold, float ratio, float attackMs, float releaseMs);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SB_SetAutoTuneEnabled(int enabled);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SB_SetAutoTuneKey(int key);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SB_SetAutoTuneScale(int scaleMask);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SB_SetAutoTuneSpeed(float speed);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SB_SetAutoTuneAmount(float amount);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern float SB_GetPeakLevel();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern float SB_GetDetectedPitch();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SB_GetDetectedNote();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SB_GetTargetNote();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SB_HasAVX2();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SB_HasSSE2();

        // Scale constants
        public const int ScaleChromatic  = 0xFFF;
        public const int ScaleMajor      = 0xAB5;
        public const int ScaleMinor      = 0x5AD;
        public const int ScalePentatonic = 0x295;

        public static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        public static readonly string[] ScaleNames = { "Chromatic", "Major", "Minor", "Pentatonic" };
        public static readonly int[] ScaleValues = { ScaleChromatic, ScaleMajor, ScaleMinor, ScalePentatonic };

        // Instance-based AutoTune API (for node graph)
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr SB_CreateAutoTune(int sampleRate);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SB_DestroyAutoTune(IntPtr handle);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SB_ProcessAutoTune(IntPtr handle, float[] buffer, int count);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SB_SetAutoTuneParamF(IntPtr handle, int param, float value);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SB_SetAutoTuneParamI(IntPtr handle, int param, int value);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern float SB_GetAutoTuneInfoF(IntPtr handle, int info);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SB_GetAutoTuneInfoI(IntPtr handle, int info);

        // Instance-based Pitch Shift API
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr SB_CreatePitchShift(int sampleRate);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SB_DestroyPitchShift(IntPtr handle);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SB_ProcessPitchShift(IntPtr handle, float[] buffer, int count, float shiftSemitones);
    }
}
