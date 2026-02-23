using System;

namespace Frontend.Core.DSP;

public static class SpectralFluxExtractor
{
    private const int NumMels  = 80;
    private const int NumLags  = 5;
    private static readonly int[] Lags = { 1, 2, 3, 4, 5 };

    /// <summary>
    /// Computes multi-lag spectral flux matching features.py calculate_multi_lag_spectral_flux exactly.
    /// Input:  mel  (n_frames, 80) — linear power (output of MelFilterbank.Apply)
    /// Output: flux (n_frames, 5)  — full-wave rectified n-th order diff, prepend zeros
    /// </summary>
    public static float[,] Compute(float[,] mel)
    {
        int nFrames = mel.GetLength(0);
        int nMels   = mel.GetLength(1);
        float[,] flux = new float[nFrames, NumLags];

        for (int li = 0; li < NumLags; li++)
        {
            int lag = Lags[li];

            // Build prepended signal: lag zero-rows + mel → shape (nFrames+lag, nMels)
            double[,] cur = new double[nFrames + lag, nMels];
            for (int f = 0; f < nFrames; f++)
                for (int m = 0; m < nMels; m++)
                    cur[lag + f, m] = mel[f, m];

            // Apply first-order diff `lag` times in place
            // np.diff(x, n=lag, prepend=zeros(lag)) produces array of length N
            int curLen = nFrames + lag;

            for (int iter = 0; iter < lag; iter++)
            {
                int nextLen = curLen - 1;
                double[,] next = new double[nextLen, nMels];
                for (int f = 0; f < nextLen; f++)
                    for (int m = 0; m < nMels; m++)
                        next[f, m] = cur[f + 1, m] - cur[f, m];
                cur = next;
                curLen = nextLen;
            }
            // cur is now (nFrames, nMels)

            // Full-wave rectify and sum across mel bins
            for (int f = 0; f < nFrames; f++)
            {
                double sum = 0.0;
                for (int m = 0; m < nMels; m++)
                    sum += Math.Abs(cur[f, m]);
                flux[f, li] = (float)sum;
            }
        }

        return flux;
    }
}
