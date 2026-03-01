#pragma once

#ifdef SOUNDBOXDSP_EXPORTS
#define SB_API __declspec(dllexport)
#else
#define SB_API __declspec(dllimport)
#endif

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// Initialization / cleanup
SB_API int  SB_Init(int sampleRate, int channels);
SB_API void SB_Shutdown(void);

// Full pipeline: applies all enabled effects in order
SB_API void SB_Process(float* buffer, int sampleCount);

// Individual effect controls
SB_API void SB_SetGainEnabled(int enabled);
SB_API void SB_SetGain(float gain);

SB_API void SB_SetNoiseGateEnabled(int enabled);
SB_API void SB_SetNoiseGateThreshold(float threshold);

SB_API void SB_SetReverbEnabled(int enabled);
SB_API void SB_SetReverbParams(float reverbTime, float mix);

SB_API void SB_SetCompressorEnabled(int enabled);
SB_API void SB_SetCompressorParams(float threshold, float ratio, float attackMs, float releaseMs);

// AutoTune
SB_API void SB_SetAutoTuneEnabled(int enabled);
SB_API void SB_SetAutoTuneKey(int key);          // 0=C, 1=C#, ... 11=B
SB_API void SB_SetAutoTuneScale(int scaleMask);   // Bitmask: bit0=C, bit1=C#, ...
SB_API void SB_SetAutoTuneSpeed(float speed);     // 0.0=instant, 1.0=off
SB_API void SB_SetAutoTuneAmount(float amount);   // 0.0=dry, 1.0=full correction

// Predefined scale constants
#define SB_SCALE_CHROMATIC  0xFFF
#define SB_SCALE_MAJOR      0xAB5
#define SB_SCALE_MINOR      0x5AD
#define SB_SCALE_PENTATONIC 0x295

// Metering (returns peak level 0..1 from last SB_Process call)
SB_API float SB_GetPeakLevel(void);

// AutoTune info (for UI display)
SB_API float SB_GetDetectedPitch(void);   // Hz, 0 if no pitch detected
SB_API int   SB_GetDetectedNote(void);    // 0-11, -1 if none
SB_API int   SB_GetTargetNote(void);      // 0-11, -1 if none

// SIMD capability query
SB_API int SB_HasAVX2(void);
SB_API int SB_HasSSE2(void);

// ============================================================
// Instance-based AutoTune API (for node graph)
// ============================================================
SB_API void* SB_CreateAutoTune(int sampleRate);
SB_API void  SB_DestroyAutoTune(void* handle);
SB_API void  SB_ProcessAutoTune(void* handle, float* buffer, int count);
SB_API void  SB_SetAutoTuneParamF(void* handle, int param, float value);
SB_API void  SB_SetAutoTuneParamI(void* handle, int param, int value);
SB_API float SB_GetAutoTuneInfoF(void* handle, int info);
SB_API int   SB_GetAutoTuneInfoI(void* handle, int info);

// AutoTune param indices
#define SB_AT_ENABLED    0
#define SB_AT_KEY        1
#define SB_AT_SCALE      2
#define SB_AT_SPEED      3
#define SB_AT_AMOUNT     4

// AutoTune info indices
#define SB_AT_INFO_PITCH_HZ      0
#define SB_AT_INFO_DETECTED_NOTE  1
#define SB_AT_INFO_TARGET_NOTE    2

// ============================================================
// Instance-based Pitch Shift API (manual pitch shift)
// ============================================================

// PitchShift state (reuses AutoTuneState but bypasses pitch detection)
SB_API void* SB_CreatePitchShift(int sampleRate);
SB_API void  SB_DestroyPitchShift(void* handle);
SB_API void  SB_ProcessPitchShift(void* handle, float* buffer, int count, float shiftSemitones);

#ifdef __cplusplus
}
#endif
