#pragma once

#include <fftw3.h>

// Scale masks (bit flags for which notes are active)
#define SCALE_CHROMATIC  0xFFF
#define SCALE_MAJOR      0xAB5
#define SCALE_MINOR      0x5AD
#define SCALE_PENTATONIC 0x295

struct AutoTuneState {
    // FFT (FFTW)
    int      fftSize;       // 2048
    int      hopSize;       // fftSize / 4 (75% overlap)
    float*   fftIn;
    fftwf_complex* fftOut;
    float*   ifftOut;
    fftwf_plan planForward;
    fftwf_plan planInverse;

    // Phase vocoder arrays
    float*   lastPhase;          // [fftSize/2+1]
    float*   sumPhase;           // [fftSize/2+1]
    float*   analysisMagnitude;  // [fftSize]
    float*   analysisFrequency;  // [fftSize]
    float*   synthMagnitude;     // [fftSize]
    float*   synthFrequency;     // [fftSize]

    // I/O buffers (smbPitchShift-style FIFO)
    float*   inFIFO;        // [fftSize]
    float*   outFIFO;       // [fftSize]
    float*   outputAccum;   // [2*fftSize]
    long     rover;
    int      sampleRate;
    bool     initialized;

    // Pitch detection scratch buffer (pre-allocated)
    float*   pitchDiff;
    int      pitchDiffSize;

    // Parameters
    bool     enabled;
    int      scaleMask;
    int      keyOffset;
    float    speed;         // 0.0 = instant, 1.0 = off
    float    amount;        // 0.0 = dry, 1.0 = fully corrected
    float    currentShift;
    bool     directShift;  // If true, bypass pitch detection and use directRatio
    float    directRatio;  // Direct pitch shift ratio (for PitchShift mode)

    // Detected pitch info (for UI)
    float    detectedPitchHz;
    int      detectedNote;
    int      targetNote;
};

void AutoTune_Init(AutoTuneState* state, int sampleRate);
void AutoTune_Destroy(AutoTuneState* state);
void AutoTune_Process(AutoTuneState* state, float* buffer, int count);

float NoteToFreq(int noteIndex, int octave);
int   FreqToNearestNote(float freq, int scaleMask, int keyOffset);
const char* NoteToName(int note);
