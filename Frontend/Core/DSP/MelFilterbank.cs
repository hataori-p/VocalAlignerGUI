using System;

namespace Frontend.Core.DSP;

/// <summary>
/// Computes a Mel filterbank matrix matching librosa.filters.mel exactly:
///   sr=16000, n_fft=400, n_mels=80, fmin=0.0, fmax=8000.0,
///   norm='slaney', htk=False
///
/// Apply() replicates librosa.feature.melspectrogram:
///   melspec = filterbank @ power.T  →  shape (n_mels, n_frames) → transposed to (n_frames, n_mels)
/// </summary>
public static class MelFilterbank
{
    public const int NumMels  = 80;
    public const float FMin   = 0.0f;
    public const float FMax   = 8000.0f;

    // Filterbank matrix: [NumMels, StftProcessor.NumBins] = [80, 201]
    private static readonly float[,] _filterbank = BuildFilterbank();

    /// <summary>
    /// Applies the mel filterbank to a power spectrum.
    /// </summary>
    /// <param name="power">Power spectrum float[numFrames, NumBins] from StftProcessor.</param>
    /// <returns>Mel spectrogram float[numFrames, NumMels].</returns>
    public static float[,] Apply(float[,] power)
    {
        int numFrames = power.GetLength(0);
        int numBins   = power.GetLength(1);
        float[,] mel  = new float[numFrames, NumMels];

        for (int f = 0; f < numFrames; f++)
            for (int m = 0; m < NumMels; m++)
            {
                double sum = 0.0;
                for (int b = 0; b < numBins; b++)
                    sum += _filterbank[m, b] * power[f, b];
                mel[f, m] = (float)sum;
            }

        return mel;
    }

    // ── Filterbank construction ────────────────────────────────────────────

    private static float[,] BuildFilterbank()
    {
        int    nMels  = NumMels;
        int    nFft   = StftProcessor.NFft;
        int    nBins  = StftProcessor.NumBins;  // 201
        int    sr     = StftProcessor.SampleRate;
        double fMin   = FMin;
        double fMax   = FMax;

        // 1. FFT bin frequencies in Hz (matches np.fft.rfftfreq(n=400, d=1/16000))
        //    fftfreqs[k] = k * sr / nFft
        double[] fftFreqs = new double[nBins];
        for (int k = 0; k < nBins; k++)
            fftFreqs[k] = k * (double)sr / nFft;

        // 2. n_mels+2 equally spaced points in mel space, converted back to Hz
        double melMin = HzToMel(fMin);
        double melMax = HzToMel(fMax);
        double[] melF = new double[nMels + 2];
        for (int i = 0; i < nMels + 2; i++)
            melF[i] = MelToHz(melMin + i * (melMax - melMin) / (nMels + 1));

        // 3. fdiff: differences between consecutive mel_f points (length nMels+1)
        double[] fdiff = new double[nMels + 1];
        for (int i = 0; i < nMels + 1; i++)
            fdiff[i] = melF[i + 1] - melF[i];

        // 4. ramps[i, k] = melF[i] - fftFreqs[k]  shape: (nMels+2, nBins)
        double[,] ramps = new double[nMels + 2, nBins];
        for (int i = 0; i < nMels + 2; i++)
            for (int k = 0; k < nBins; k++)
                ramps[i, k] = melF[i] - fftFreqs[k];

        // 5. Build triangle filters in Hz domain
        float[,] fb = new float[nMels, nBins];
        for (int m = 0; m < nMels; m++)
        {
            for (int k = 0; k < nBins; k++)
            {
                // Rising slope: -ramps[m,k] / fdiff[m]
                double lower = -ramps[m,     k] / fdiff[m];
                // Falling slope: ramps[m+2,k] / fdiff[m+1]
                double upper =  ramps[m + 2, k] / fdiff[m + 1];
                // Triangle weight: max(0, min(lower, upper))
                double w = Math.Max(0.0, Math.Min(lower, upper));
                fb[m, k] = (float)w;
            }

            // 6. Slaney normalization: 2.0 / (melF[m+2] - melF[m])
            double bandwidth = melF[m + 2] - melF[m];
            if (bandwidth > 1e-10)
            {
                double enorm = 2.0 / bandwidth;
                for (int k = 0; k < nBins; k++)
                    fb[m, k] = (float)(fb[m, k] * enorm);
            }
        }

        return fb;
    }

    // ── Mel scale conversions (Slaney/Auditory Toolbox, htk=False) ────────────
    // This is a two-piece scale: linear below 1000 Hz, log above.
    // Matches librosa.hz_to_mel / librosa.mel_to_hz with htk=False exactly.

    private const double LinSlope  = 3.0 / 200.0;       // linear region slope
    private const double LinBreak  = 1000.0;             // break point in Hz
    private const double LinMel    = 15.0;               // mel value at break point (1000 * 3/200)
    private const double LogStep   = 27.0;               // log region step
    private const double LogBase   = 6.4;                // log region base

    // hz → mel
    private static double HzToMel(double hz)
    {
        if (hz < LinBreak)
            return hz * LinSlope;
        return LinMel + LogStep * Math.Log(hz / LinBreak) / Math.Log(LogBase);
    }

    // mel → hz
    private static double MelToHz(double mel)
    {
        if (mel < LinMel)
            return mel / LinSlope;
        return LinBreak * Math.Pow(LogBase, (mel - LinMel) / LogStep);
    }
}
