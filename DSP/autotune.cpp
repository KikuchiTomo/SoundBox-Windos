#include "autotune.h"
#include <cmath>
#include <cstring>
#include <cstdlib>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

// ============================================================
// Musical utilities
// ============================================================

static const char* g_noteNames[] = {
    "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"
};

float NoteToFreq(int noteIndex, int octave)
{
    int midi = noteIndex + (octave + 1) * 12;
    return 440.0f * powf(2.0f, (midi - 69) / 12.0f);
}

const char* NoteToName(int note)
{
    if (note < 0 || note > 11) return "?";
    return g_noteNames[note];
}

int FreqToNearestNote(float freq, int scaleMask, int keyOffset)
{
    if (freq <= 0) return -1;

    float midiNote = 69.0f + 12.0f * log2f(freq / 440.0f);
    if (midiNote < 0 || midiNote > 127) return -1;

    int noteInOctave = ((int)roundf(midiNote)) % 12;
    int adjusted = (noteInOctave - keyOffset + 12) % 12;

    int bestNote = adjusted;
    int bestDist = 12;

    for (int i = 0; i < 12; i++)
    {
        if (scaleMask & (1 << i))
        {
            int dist = abs(adjusted - i);
            if (dist > 6) dist = 12 - dist;
            if (dist < bestDist)
            {
                bestDist = dist;
                bestNote = i;
            }
        }
    }

    return (bestNote + keyOffset) % 12;
}

// ============================================================
// Pitch detection (YIN algorithm, no dynamic allocation)
// ============================================================
static float DetectPitch(AutoTuneState* state, const float* buffer, int size)
{
    int halfSize = size / 2;
    if (halfSize > state->pitchDiffSize) halfSize = state->pitchDiffSize;
    float* diff = state->pitchDiff;

    // Step 1: Squared difference function
    for (int tau = 0; tau < halfSize; tau++)
    {
        float sum = 0;
        for (int j = 0; j < halfSize; j++)
        {
            float d = buffer[j] - buffer[j + tau];
            sum += d * d;
        }
        diff[tau] = sum;
    }

    // Step 2: Cumulative mean normalized difference
    diff[0] = 1.0f;
    float runningSum = 0;
    for (int tau = 1; tau < halfSize; tau++)
    {
        runningSum += diff[tau];
        if (runningSum < 1e-12f)
            diff[tau] = 1.0f;
        else
            diff[tau] = diff[tau] * tau / runningSum;
    }

    // Step 3: Find first dip below threshold
    float threshold = 0.15f;
    int sampleRate = state->sampleRate;
    int minTau = sampleRate / 800;  // max ~800 Hz
    int maxTau = sampleRate / 60;   // min ~60 Hz
    if (maxTau >= halfSize) maxTau = halfSize - 1;

    int bestTau = -1;
    for (int tau = minTau; tau < maxTau; tau++)
    {
        if (diff[tau] < threshold)
        {
            // Find the minimum in this dip
            while (tau + 1 < maxTau && diff[tau + 1] < diff[tau])
                tau++;
            bestTau = tau;
            break;
        }
    }

    if (bestTau < 1) return 0;

    // Step 4: Parabolic interpolation
    float s0 = diff[bestTau - 1];
    float s1 = diff[bestTau];
    float s2 = (bestTau + 1 < halfSize) ? diff[bestTau + 1] : s1;
    float denom = 2.0f * (2.0f * s1 - s2 - s0);
    float shift = 0;
    if (fabsf(denom) > 1e-12f)
        shift = (s0 - s2) / denom;

    float refinedTau = bestTau + shift;
    if (refinedTau <= 0) return 0;
    return (float)sampleRate / refinedTau;
}

// ============================================================
// Init / Destroy
// ============================================================

void AutoTune_Init(AutoTuneState* state, int sampleRate)
{
    memset(state, 0, sizeof(AutoTuneState));

    state->sampleRate = sampleRate;
    state->fftSize = 2048;
    state->hopSize = state->fftSize / 4;
    state->enabled = false;
    state->scaleMask = SCALE_CHROMATIC;
    state->keyOffset = 0;
    state->speed = 0.0f;
    state->amount = 1.0f;
    state->currentShift = 1.0f;
    state->directShift = false;
    state->directRatio = 1.0f;
    state->rover = 0;
    state->initialized = false;

    int N = state->fftSize;
    int halfN = N / 2 + 1;

    // FFTW allocations
    state->fftIn  = fftwf_alloc_real(N);
    state->fftOut = fftwf_alloc_complex(halfN);
    state->ifftOut = fftwf_alloc_real(N);

    state->planForward = fftwf_plan_dft_r2c_1d(N, state->fftIn, state->fftOut, FFTW_MEASURE);
    state->planInverse = fftwf_plan_dft_c2r_1d(N, state->fftOut, state->ifftOut, FFTW_MEASURE);

    // Phase vocoder arrays
    state->lastPhase          = (float*)calloc(halfN, sizeof(float));
    state->sumPhase           = (float*)calloc(halfN, sizeof(float));
    state->analysisMagnitude  = (float*)calloc(N, sizeof(float));
    state->analysisFrequency  = (float*)calloc(N, sizeof(float));
    state->synthMagnitude     = (float*)calloc(N, sizeof(float));
    state->synthFrequency     = (float*)calloc(N, sizeof(float));

    // I/O FIFO buffers (smbPitchShift style)
    state->inFIFO      = (float*)calloc(N, sizeof(float));
    state->outFIFO     = (float*)calloc(N, sizeof(float));
    state->outputAccum = (float*)calloc(2 * N, sizeof(float));

    // Pitch detection buffer
    state->pitchDiffSize = N / 2;
    state->pitchDiff = (float*)calloc(state->pitchDiffSize, sizeof(float));
}

void AutoTune_Destroy(AutoTuneState* state)
{
    if (state->planForward) fftwf_destroy_plan(state->planForward);
    if (state->planInverse) fftwf_destroy_plan(state->planInverse);
    if (state->fftIn)   fftwf_free(state->fftIn);
    if (state->fftOut)  fftwf_free(state->fftOut);
    if (state->ifftOut) fftwf_free(state->ifftOut);
    free(state->lastPhase);
    free(state->sumPhase);
    free(state->analysisMagnitude);
    free(state->analysisFrequency);
    free(state->synthMagnitude);
    free(state->synthFrequency);
    free(state->inFIFO);
    free(state->outFIFO);
    free(state->outputAccum);
    free(state->pitchDiff);
    memset(state, 0, sizeof(AutoTuneState));
}

// ============================================================
// Process (based on smbPitchShift by Stephan M. Bernsee)
// ============================================================

void AutoTune_Process(AutoTuneState* state, float* buffer, int count)
{
    if (!state->enabled) return;

    int N = state->fftSize;
    int halfN = N / 2 + 1;
    int stepSize = state->hopSize;
    int osamp = N / stepSize;  // oversampling factor (4)
    int inFifoLatency = N - stepSize;
    double freqPerBin = (double)state->sampleRate / (double)N;
    double expct = 2.0 * M_PI * (double)stepSize / (double)N;

    // First-time initialization
    if (!state->initialized)
    {
        state->rover = inFifoLatency;
        memset(state->inFIFO, 0, N * sizeof(float));
        memset(state->outFIFO, 0, N * sizeof(float));
        memset(state->outputAccum, 0, 2 * N * sizeof(float));
        memset(state->lastPhase, 0, halfN * sizeof(float));
        memset(state->sumPhase, 0, halfN * sizeof(float));
        state->initialized = true;
    }

    for (int i = 0; i < count; i++)
    {
        float dry = buffer[i];

        // Push into input FIFO, read from output FIFO
        state->inFIFO[state->rover] = dry;
        float wet = state->outFIFO[state->rover - inFifoLatency];
        state->rover++;

        // When we have a full frame, process it
        if (state->rover >= N)
        {
            state->rover = inFifoLatency;

            float pitchShift;
            if (state->directShift)
            {
                // Direct pitch shift mode: bypass pitch detection
                pitchShift = state->directRatio;
            }
            else
            {
                // --- Pitch detection ---
                float detectedHz = DetectPitch(state, state->inFIFO, N);
                state->detectedPitchHz = detectedHz;

                // --- Calculate target pitch shift ---
                float targetShift = 1.0f;
                if (detectedHz > 60.0f && detectedHz < 800.0f)
                {
                    float midiNote = 69.0f + 12.0f * log2f(detectedHz / 440.0f);
                    int noteInOctave = ((int)roundf(midiNote)) % 12;
                    state->detectedNote = noteInOctave;

                    int targetNote = FreqToNearestNote(detectedHz, state->scaleMask, state->keyOffset);
                    state->targetNote = targetNote;

                    if (targetNote >= 0)
                    {
                        int octave = (int)(midiNote / 12) - 1;
                        int targetMidi = targetNote + (octave + 1) * 12;

                        // Choose closest octave
                        if (targetMidi - (int)roundf(midiNote) > 6)
                            targetMidi -= 12;
                        else if ((int)roundf(midiNote) - targetMidi > 6)
                            targetMidi += 12;

                        float targetHz = 440.0f * powf(2.0f, (targetMidi - 69) / 12.0f);
                        targetShift = targetHz / detectedHz;
                    }
                }

                // Smooth pitch shift transition
                float smoothing = state->speed * 0.99f;
                state->currentShift = state->currentShift * smoothing + targetShift * (1.0f - smoothing);
                pitchShift = 1.0f + (state->currentShift - 1.0f) * state->amount;
            }

            // =========================================
            // ANALYSIS: Windowing + FFT
            // =========================================
            for (int k = 0; k < N; k++)
            {
                double window = -0.5 * cos(2.0 * M_PI * (double)k / (double)N) + 0.5;
                state->fftIn[k] = (float)(state->inFIFO[k] * window);
            }

            fftwf_execute(state->planForward);

            // Convert to magnitude + true frequency
            for (int k = 0; k < halfN; k++)
            {
                double real = state->fftOut[k][0];
                double imag = state->fftOut[k][1];
                double magn = 2.0 * sqrt(real * real + imag * imag);
                double phase = atan2(imag, real);

                // Phase difference from last frame
                double tmp = phase - (double)state->lastPhase[k];
                state->lastPhase[k] = (float)phase;

                // Subtract expected phase advance
                tmp -= (double)k * expct;

                // Map to [-pi, pi] using integer method (from smbPitchShift)
                long qpd = (long)(tmp / M_PI);
                if (qpd >= 0) qpd += qpd & 1;
                else qpd -= qpd & 1;
                tmp -= M_PI * (double)qpd;

                // Deviation from bin frequency -> true frequency
                tmp = (double)osamp * tmp / (2.0 * M_PI);
                tmp = (double)k * freqPerBin + tmp * freqPerBin;

                state->analysisMagnitude[k] = (float)magn;
                state->analysisFrequency[k] = (float)tmp;
            }

            // =========================================
            // PITCH SHIFTING: Remap frequency bins
            // =========================================
            memset(state->synthMagnitude, 0, N * sizeof(float));
            memset(state->synthFrequency, 0, N * sizeof(float));

            for (int k = 0; k < halfN; k++)
            {
                int index = (int)((double)k * pitchShift);
                if (index >= 0 && index < halfN)
                {
                    state->synthMagnitude[index] += state->analysisMagnitude[k];
                    state->synthFrequency[index] = state->analysisFrequency[k] * pitchShift;
                }
            }

            // =========================================
            // SYNTHESIS: Frequency -> Phase + IFFT
            // =========================================
            for (int k = 0; k < halfN; k++)
            {
                double magn = state->synthMagnitude[k];
                double tmp = state->synthFrequency[k];

                // Convert true frequency back to phase increment
                tmp -= (double)k * freqPerBin;
                tmp /= freqPerBin;
                tmp = 2.0 * M_PI * tmp / (double)osamp;
                tmp += (double)k * expct;

                state->sumPhase[k] += (float)tmp;
                double phase = state->sumPhase[k];

                state->fftOut[k][0] = (float)(magn * cos(phase));
                state->fftOut[k][1] = (float)(magn * sin(phase));
            }

            fftwf_execute(state->planInverse);

            // =========================================
            // OVERLAP-ADD into output accumulator
            // FFTW c2r reconstructs conjugate symmetry automatically,
            // so we DON'T need the 2.0 factor from original smbPitchShift
            // (which compensated for zeroed negative frequency bins).
            // =========================================
            int fftFrameSize2 = N / 2;
            for (int k = 0; k < N; k++)
            {
                double window = -0.5 * cos(2.0 * M_PI * (double)k / (double)N) + 0.5;
                state->outputAccum[k] += (float)(window * (double)state->ifftOut[k]
                                         / ((double)fftFrameSize2 * (double)osamp));
            }

            // Copy first stepSize samples to output FIFO
            for (int k = 0; k < stepSize; k++)
                state->outFIFO[k] = state->outputAccum[k];

            // Shift output accumulator left by stepSize
            memmove(state->outputAccum, state->outputAccum + stepSize, N * sizeof(float));
            memset(state->outputAccum + N, 0, stepSize * sizeof(float));

            // Shift input FIFO (keep overlap portion)
            memmove(state->inFIFO, state->inFIFO + stepSize, (size_t)inFifoLatency * sizeof(float));
        }

        // Output: blend dry and wet
        buffer[i] = dry * (1.0f - state->amount) + wet * state->amount;
    }
}
