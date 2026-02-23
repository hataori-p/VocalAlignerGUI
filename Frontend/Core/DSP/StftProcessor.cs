using System;

namespace Frontend.Core.DSP;

/// <summary>
/// Computes a Short-Time Fourier Transform power spectrum that matches
/// librosa.feature.melspectrogram defaults exactly:
///   sr=16000, n_fft=400, hop_length=80, win_length=400,
///   window='hann', center=True, power=2.0
///
/// Uses a direct DFT for exactly n_fft=400 points (non-power-of-2),
/// producing n_fft/2+1 = 201 one-sided bins — identical to numpy.fft.rfft(frame, n=400).
/// </summary>
public static class StftProcessor
{
    // ── Public constants (must match features.py) ──────────────────────────
    public const int SampleRate = 16000;
    public const int WinLength  = 400;   // samples  (25 ms @ 16 kHz)
    public const int HopLength  = 80;    // samples  ( 5 ms @ 16 kHz)
    public const int NFft       = 400;   // DFT size (matches librosa n_fft)
    public const int NumBins    = 201;   // NFft/2 + 1  (one-sided rfft output)

    // Pre-computed Hann window (periodic, length = WinLength)
    // Matches scipy.signal.get_window('hann', N, fftbins=True)
    // Formula: w[i] = 0.5 * (1 - cos(2*pi*i / N))  — NOT (N-1) in denominator
    private static readonly float[] _hann = BuildHann(WinLength);

    // Pre-computed DFT cosine/sine tables for N=400, k in [0, NumBins)
    // cos_table[k, n] = cos(2*pi*k*n / NFft)
    // sin_table[k, n] = sin(2*pi*k*n / NFft)
    private static readonly double[,] _cosTable = BuildCosTable();
    private static readonly double[,] _sinTable = BuildSinTable();

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the power spectrogram of <paramref name="audio"/>.
    /// Replicates librosa center=True zero-padding (pad_mode="constant").
    /// </summary>
    /// <param name="audio">Mono 16 kHz float samples.</param>
    /// <returns>
    /// Power spectrum: float[numFrames, NumBins].
    /// numFrames = 1 + floor(audio.Length / HopLength)
    /// </returns>
    public static float[,] ComputePowerSpectrum(float[] audio)
    {
        // 1. Zero-pad by NFft/2 on each side (matches librosa default pad_mode="constant")
        int padN = NFft / 2;
        float[] padded = new float[audio.Length + 2 * padN];
        Array.Copy(audio, 0, padded, padN, audio.Length);

        // 2. Determine frame count (matches librosa formula)
        int numFrames = 1 + (audio.Length / HopLength);

        float[,] power = new float[numFrames, NumBins];

        // Frame buffer (windowed samples)
        double[] frame = new double[NFft];

        for (int f = 0; f < numFrames; f++)
        {
            int start = f * HopLength;

            // 3. Fill frame: apply Hann window, zero-pad remainder
            for (int i = 0; i < NFft; i++)
            {
                int si = start + i;
                double sample = (si < padded.Length) ? padded[si] : 0.0;
                // WinLength == NFft here, so every sample gets windowed
                frame[i] = sample * _hann[i];
            }

            // 4. Direct DFT for k in [0, NumBins)
            // X[k] = sum_{n=0}^{N-1} frame[n] * exp(-j*2*pi*k*n/N)
            //      = sum_n frame[n] * (cos(2pi*k*n/N) - j*sin(2pi*k*n/N))
            for (int k = 0; k < NumBins; k++)
            {
                double re = 0.0, im = 0.0;
                for (int n = 0; n < NFft; n++)
                {
                    re += frame[n] * _cosTable[k, n];
                    im -= frame[n] * _sinTable[k, n];
                }
                power[f, k] = (float)(re * re + im * im);
            }
        }

        return power;
    }

    // ── Private helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Periodic Hann window of length <paramref name="n"/>.
    /// w[i] = 0.5 * (1 - cos(2*pi*i / n))
    /// </summary>
    private static float[] BuildHann(int n)
    {
        float[] w = new float[n];
        for (int i = 0; i < n; i++)
            w[i] = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / n)));
        return w;
    }

    /// <summary>
    /// Pre-computes cos(2*pi*k*n / NFft) for k in [0,NumBins), n in [0,NFft).
    /// </summary>
    private static double[,] BuildCosTable()
    {
        double[,] t = new double[NumBins, NFft];
        for (int k = 0; k < NumBins; k++)
            for (int n = 0; n < NFft; n++)
                t[k, n] = Math.Cos(2.0 * Math.PI * k * n / NFft);
        return t;
    }

    /// <summary>
    /// Pre-computes sin(2*pi*k*n / NFft) for k in [0,NumBins), n in [0,NFft).
    /// </summary>
    private static double[,] BuildSinTable()
    {
        double[,] t = new double[NumBins, NFft];
        for (int k = 0; k < NumBins; k++)
            for (int n = 0; n < NFft; n++)
                t[k, n] = Math.Sin(2.0 * Math.PI * k * n / NFft);
        return t;
    }
}
