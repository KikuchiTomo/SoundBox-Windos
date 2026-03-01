#include "soundbox_dsp.h"
#include "autotune.h"

#include <immintrin.h>
#include <intrin.h>
#include <cmath>
#include <cstring>
#include <algorithm>

// ============================================================
// SIMD capability detection
// ============================================================
static bool g_hasSSE2 = false;
static bool g_hasAVX2 = false;

static void DetectSIMD()
{
    int cpuInfo[4];
    __cpuid(cpuInfo, 1);
    g_hasSSE2 = (cpuInfo[3] & (1 << 26)) != 0;

    __cpuidex(cpuInfo, 7, 0);
    g_hasAVX2 = (cpuInfo[1] & (1 << 5)) != 0;
}

// ============================================================
// Global state
// ============================================================
static int    g_sampleRate = 48000;
static int    g_channels   = 2;
static float  g_peakLevel  = 0.0f;

// --- Gain ---
static bool   g_gainEnabled = true;
static float  g_gainLevel   = 1.0f;

// --- Noise Gate ---
static bool   g_ngEnabled     = false;
static float  g_ngThreshold   = 0.01f;
static bool   g_ngGateOpen    = false;
static int    g_ngHoldCounter = 0;

// --- Reverb (Schroeder: 4 comb + 2 allpass) ---
static bool   g_reverbEnabled = false;
static float  g_reverbTime    = 1.5f;  // RT60 in seconds
static float  g_reverbMix     = 0.3f;  // Wet/dry mix

// Comb filter state
static const int NUM_COMBS = 4;
static const int COMB_DELAYS[] = { 1557, 1617, 1491, 1422 };  // Mutually prime delays

struct CombFilter {
    float* buffer;
    int    size;
    int    pos;
    float  filterStore;  // Lowpass state for damping
};
static CombFilter g_combs[NUM_COMBS] = {};

// Allpass filter state
static const int NUM_ALLPASS = 2;
static const int ALLPASS_DELAYS[] = { 225, 556 };
static const float ALLPASS_COEFF = 0.5f;

struct AllpassFilter {
    float* buffer;
    int    size;
    int    pos;
};
static AllpassFilter g_allpass[NUM_ALLPASS] = {};

// --- Compressor ---
static bool   g_compEnabled    = false;
static float  g_compThreshold  = 0.5f;
static float  g_compRatio      = 4.0f;
static float  g_compAttackMs   = 5.0f;
static float  g_compReleaseMs  = 50.0f;
static float  g_compEnvelope   = 0.0f;

// --- AutoTune ---
static AutoTuneState g_autoTune = {};

// ============================================================
// Reverb helpers
// ============================================================
static void ReverbInit()
{
    // Scale delays by sample rate ratio (base delays are for 48kHz)
    float srScale = (float)g_sampleRate / 48000.0f;

    for (int i = 0; i < NUM_COMBS; i++)
    {
        int size = (int)(COMB_DELAYS[i] * srScale);
        if (size < 1) size = 1;
        g_combs[i].buffer = new float[size]();
        g_combs[i].size = size;
        g_combs[i].pos = 0;
        g_combs[i].filterStore = 0.0f;
    }

    for (int i = 0; i < NUM_ALLPASS; i++)
    {
        int size = (int)(ALLPASS_DELAYS[i] * srScale);
        if (size < 1) size = 1;
        g_allpass[i].buffer = new float[size]();
        g_allpass[i].size = size;
        g_allpass[i].pos = 0;
    }
}

static void ReverbCleanup()
{
    for (int i = 0; i < NUM_COMBS; i++)
    {
        delete[] g_combs[i].buffer;
        g_combs[i].buffer = nullptr;
    }
    for (int i = 0; i < NUM_ALLPASS; i++)
    {
        delete[] g_allpass[i].buffer;
        g_allpass[i].buffer = nullptr;
    }
}

// Compute feedback coefficient for a comb filter to achieve RT60
static float ComputeFeedback(int delaySamples, float rt60)
{
    if (rt60 <= 0.01f) return 0.0f;
    // g = 10^(-3 * delay / (RT60 * sampleRate))
    return powf(10.0f, -3.0f * delaySamples / (rt60 * g_sampleRate));
}

static void ProcessReverb(float* buf, int count)
{
    if (!g_reverbEnabled) return;
    if (g_combs[0].buffer == nullptr) return;

    float damping = 0.3f;  // Lowpass damping in comb filters
    float dampingInv = 1.0f - damping;

    for (int i = 0; i < count; i++)
    {
        float input = buf[i];
        float combSum = 0.0f;

        // 4 parallel comb filters (with lowpass damping)
        for (int c = 0; c < NUM_COMBS; c++)
        {
            CombFilter& cf = g_combs[c];
            float feedback = ComputeFeedback(cf.size, g_reverbTime);

            float delayed = cf.buffer[cf.pos];

            // Lowpass filter on feedback (simulates air absorption)
            cf.filterStore = delayed * dampingInv + cf.filterStore * damping;

            // Write back with feedback
            cf.buffer[cf.pos] = input + cf.filterStore * feedback;
            cf.pos = (cf.pos + 1) % cf.size;

            combSum += delayed;
        }

        // Average comb outputs
        float out = combSum * 0.25f;

        // 2 series allpass filters (add density/diffusion)
        for (int a = 0; a < NUM_ALLPASS; a++)
        {
            AllpassFilter& ap = g_allpass[a];
            float delayed = ap.buffer[ap.pos];

            float v = out + delayed * (-ALLPASS_COEFF);
            ap.buffer[ap.pos] = out + delayed * ALLPASS_COEFF;
            // Simplified allpass: output = -coeff*input + delayed
            out = v;
            ap.pos = (ap.pos + 1) % ap.size;
        }

        // Wet/dry mix
        buf[i] = input * (1.0f - g_reverbMix) + out * g_reverbMix;

        // Clamp
        if (buf[i] > 1.0f)  buf[i] = 1.0f;
        if (buf[i] < -1.0f) buf[i] = -1.0f;
    }
}

// ============================================================
// SIMD Gain Processing
// ============================================================
static void ProcessGain_AVX2(float* buf, int count, float gain)
{
#ifdef __AVX2__
    __m256 vGain = _mm256_set1_ps(gain);
    __m256 vMin  = _mm256_set1_ps(-1.0f);
    __m256 vMax  = _mm256_set1_ps(1.0f);

    int i = 0;
    for (; i + 8 <= count; i += 8)
    {
        __m256 v = _mm256_loadu_ps(buf + i);
        v = _mm256_mul_ps(v, vGain);
        v = _mm256_max_ps(v, vMin);
        v = _mm256_min_ps(v, vMax);
        _mm256_storeu_ps(buf + i, v);
    }
    for (; i < count; i++)
    {
        buf[i] *= gain;
        if (buf[i] > 1.0f)  buf[i] = 1.0f;
        if (buf[i] < -1.0f) buf[i] = -1.0f;
    }
#endif
}

static void ProcessGain_SSE2(float* buf, int count, float gain)
{
    __m128 vGain = _mm_set1_ps(gain);
    __m128 vMin  = _mm_set1_ps(-1.0f);
    __m128 vMax  = _mm_set1_ps(1.0f);

    int i = 0;
    for (; i + 4 <= count; i += 4)
    {
        __m128 v = _mm_loadu_ps(buf + i);
        v = _mm_mul_ps(v, vGain);
        v = _mm_max_ps(v, vMin);
        v = _mm_min_ps(v, vMax);
        _mm_storeu_ps(buf + i, v);
    }
    for (; i < count; i++)
    {
        buf[i] *= gain;
        if (buf[i] > 1.0f)  buf[i] = 1.0f;
        if (buf[i] < -1.0f) buf[i] = -1.0f;
    }
}

static void ProcessGain(float* buf, int count)
{
    if (!g_gainEnabled) return;
    if (g_gainLevel == 1.0f) return;

    if (g_hasAVX2)
        ProcessGain_AVX2(buf, count, g_gainLevel);
    else
        ProcessGain_SSE2(buf, count, g_gainLevel);
}

// ============================================================
// Noise Gate
// ============================================================
static void ProcessNoiseGate(float* buf, int count)
{
    if (!g_ngEnabled) return;
    int holdSamples = g_sampleRate / 10;

    for (int i = 0; i < count; i++)
    {
        float absVal = fabsf(buf[i]);
        if (absVal > g_ngThreshold)
        {
            g_ngGateOpen = true;
            g_ngHoldCounter = holdSamples;
        }
        else if (g_ngHoldCounter > 0)
        {
            g_ngHoldCounter--;
        }
        else
        {
            g_ngGateOpen = false;
        }
        if (!g_ngGateOpen) buf[i] = 0.0f;
    }
}

// ============================================================
// Compressor
// ============================================================
static void ProcessCompressor(float* buf, int count)
{
    if (!g_compEnabled) return;

    float attackCoeff  = expf(-1.0f / (g_compAttackMs  * 0.001f * g_sampleRate));
    float releaseCoeff = expf(-1.0f / (g_compReleaseMs * 0.001f * g_sampleRate));

    for (int i = 0; i < count; i++)
    {
        float absVal = fabsf(buf[i]);

        if (absVal > g_compEnvelope)
            g_compEnvelope = attackCoeff * g_compEnvelope + (1.0f - attackCoeff) * absVal;
        else
            g_compEnvelope = releaseCoeff * g_compEnvelope + (1.0f - releaseCoeff) * absVal;

        if (g_compEnvelope > g_compThreshold)
        {
            float dbOver = 20.0f * log10f(g_compEnvelope / g_compThreshold);
            float dbReduction = dbOver * (1.0f - 1.0f / g_compRatio);
            float gainReduction = powf(10.0f, -dbReduction / 20.0f);
            buf[i] *= gainReduction;
        }
    }
}

// ============================================================
// Peak Level Metering (SIMD)
// ============================================================
static float ComputePeak_SSE2(const float* buf, int count)
{
    __m128 vPeak = _mm_setzero_ps();
    __m128 vSign = _mm_set1_ps(-0.0f);

    int i = 0;
    for (; i + 4 <= count; i += 4)
    {
        __m128 v = _mm_loadu_ps(buf + i);
        v = _mm_andnot_ps(vSign, v);
        vPeak = _mm_max_ps(vPeak, v);
    }

    float tmp[4];
    _mm_storeu_ps(tmp, vPeak);
    float peak = 0.0f;
    for (int j = 0; j < 4; j++)
        if (tmp[j] > peak) peak = tmp[j];

    for (; i < count; i++)
    {
        float a = fabsf(buf[i]);
        if (a > peak) peak = a;
    }
    return peak;
}

// ============================================================
// Public API
// ============================================================
SB_API int SB_Init(int sampleRate, int channels)
{
    DetectSIMD();
    g_sampleRate = sampleRate;
    g_channels   = channels;
    g_peakLevel  = 0.0f;
    g_ngGateOpen = false;
    g_ngHoldCounter = 0;
    g_compEnvelope = 0.0f;

    ReverbCleanup();
    ReverbInit();

    AutoTune_Destroy(&g_autoTune);
    AutoTune_Init(&g_autoTune, sampleRate);

    return 1;
}

SB_API void SB_Shutdown(void)
{
    ReverbCleanup();
    AutoTune_Destroy(&g_autoTune);
}

SB_API void SB_Process(float* buffer, int sampleCount)
{
    ProcessGain(buffer, sampleCount);
    ProcessNoiseGate(buffer, sampleCount);
    ProcessCompressor(buffer, sampleCount);
    AutoTune_Process(&g_autoTune, buffer, sampleCount);
    ProcessReverb(buffer, sampleCount);
    g_peakLevel = ComputePeak_SSE2(buffer, sampleCount);
}

SB_API void SB_SetGainEnabled(int enabled)        { g_gainEnabled = enabled != 0; }
SB_API void SB_SetGain(float gain)                 { g_gainLevel = gain; }

SB_API void SB_SetNoiseGateEnabled(int enabled)    { g_ngEnabled = enabled != 0; }
SB_API void SB_SetNoiseGateThreshold(float t)      { g_ngThreshold = t; }

SB_API void SB_SetReverbEnabled(int enabled)       { g_reverbEnabled = enabled != 0; }
SB_API void SB_SetReverbParams(float reverbTime, float mix)
{
    g_reverbTime = reverbTime;
    g_reverbMix  = mix;
}

SB_API void SB_SetCompressorEnabled(int enabled)   { g_compEnabled = enabled != 0; }
SB_API void SB_SetCompressorParams(float threshold, float ratio, float attackMs, float releaseMs)
{
    g_compThreshold = threshold;
    g_compRatio     = ratio;
    g_compAttackMs  = attackMs;
    g_compReleaseMs = releaseMs;
}

SB_API void SB_SetAutoTuneEnabled(int enabled)   { g_autoTune.enabled = enabled != 0; }
SB_API void SB_SetAutoTuneKey(int key)           { g_autoTune.keyOffset = key % 12; }
SB_API void SB_SetAutoTuneScale(int scaleMask)   { g_autoTune.scaleMask = scaleMask; }
SB_API void SB_SetAutoTuneSpeed(float speed)     { g_autoTune.speed = speed; }
SB_API void SB_SetAutoTuneAmount(float amount)   { g_autoTune.amount = amount; }

SB_API float SB_GetPeakLevel(void)       { return g_peakLevel; }
SB_API float SB_GetDetectedPitch(void)   { return g_autoTune.detectedPitchHz; }
SB_API int   SB_GetDetectedNote(void)    { return g_autoTune.detectedNote; }
SB_API int   SB_GetTargetNote(void)      { return g_autoTune.targetNote; }
SB_API int   SB_HasAVX2(void)            { return g_hasAVX2 ? 1 : 0; }
SB_API int   SB_HasSSE2(void)            { return g_hasSSE2 ? 1 : 0; }

// ============================================================
// Instance-based AutoTune API
// ============================================================
SB_API void* SB_CreateAutoTune(int sampleRate)
{
    AutoTuneState* state = new AutoTuneState();
    AutoTune_Init(state, sampleRate);
    state->enabled = true;
    return state;
}

SB_API void SB_DestroyAutoTune(void* handle)
{
    if (!handle) return;
    AutoTuneState* state = (AutoTuneState*)handle;
    AutoTune_Destroy(state);
    delete state;
}

SB_API void SB_ProcessAutoTune(void* handle, float* buffer, int count)
{
    if (!handle) return;
    AutoTuneState* state = (AutoTuneState*)handle;
    AutoTune_Process(state, buffer, count);
}

SB_API void SB_SetAutoTuneParamF(void* handle, int param, float value)
{
    if (!handle) return;
    AutoTuneState* state = (AutoTuneState*)handle;
    switch (param) {
        case 3: state->speed = value; break;
        case 4: state->amount = value; break;
    }
}

SB_API void SB_SetAutoTuneParamI(void* handle, int param, int value)
{
    if (!handle) return;
    AutoTuneState* state = (AutoTuneState*)handle;
    switch (param) {
        case 0: state->enabled = value != 0; break;
        case 1: state->keyOffset = value % 12; break;
        case 2: state->scaleMask = value; break;
    }
}

SB_API float SB_GetAutoTuneInfoF(void* handle, int info)
{
    if (!handle) return 0.0f;
    AutoTuneState* state = (AutoTuneState*)handle;
    switch (info) {
        case 0: return state->detectedPitchHz;
        default: return 0.0f;
    }
}

SB_API int SB_GetAutoTuneInfoI(void* handle, int info)
{
    if (!handle) return -1;
    AutoTuneState* state = (AutoTuneState*)handle;
    switch (info) {
        case 1: return state->detectedNote;
        case 2: return state->targetNote;
        default: return -1;
    }
}

// ============================================================
// Instance-based Pitch Shift API
// (Reuses AutoTune phase vocoder but sets pitch shift directly)
// ============================================================
SB_API void* SB_CreatePitchShift(int sampleRate)
{
    AutoTuneState* state = new AutoTuneState();
    AutoTune_Init(state, sampleRate);
    state->enabled = true;
    state->directShift = true;  // Bypass pitch detection
    state->directRatio = 1.0f;
    state->amount = 1.0f;
    return state;
}

SB_API void SB_DestroyPitchShift(void* handle)
{
    SB_DestroyAutoTune(handle);
}

SB_API void SB_ProcessPitchShift(void* handle, float* buffer, int count, float shiftSemitones)
{
    if (!handle) return;
    AutoTuneState* state = (AutoTuneState*)handle;

    // Convert semitones to pitch shift ratio
    float ratio = powf(2.0f, shiftSemitones / 12.0f);

    // Direct pitch shift: bypass pitch detection entirely
    state->directShift = true;
    state->directRatio = ratio;
    state->enabled = true;
    state->amount = 1.0f;

    AutoTune_Process(state, buffer, count);
}
