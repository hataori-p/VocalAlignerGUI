using System;

namespace Frontend.Core.DSP;

public static class PpgUpsampler
{
    /// <summary>
    /// Linearly interpolates a PPG array along the time axis from T_src frames
    /// to targetLen frames. Matches scipy.interpolate.interp1d with kind='linear'
    /// and fill_value='extrapolate' using np.linspace(0,1) parameterization.
    /// Input:  ppg (T_src, numPhonemes)
    /// Output: (targetLen, numPhonemes)
    /// </summary>
    public static float[,] Upsample(float[,] ppg, int targetLen)
    {
        int srcLen      = ppg.GetLength(0);
        int numPhonemes = ppg.GetLength(1);

        if (srcLen == targetLen) return ppg;
        if (targetLen <= 0) throw new ArgumentOutOfRangeException(nameof(targetLen));

        float[,] output = new float[targetLen, numPhonemes];

        // Special case: single source frame — replicate
        if (srcLen == 1)
        {
            for (int n = 0; n < targetLen; n++)
                for (int p = 0; p < numPhonemes; p++)
                    output[n, p] = ppg[0, p];
            return output;
        }

        for (int n = 0; n < targetLen; n++)
        {
            // Map output index n to source position using linspace(0,1) on both sides
            // t in [0, srcLen-1]
            double t  = (double)n * (srcLen - 1) / (targetLen - 1);
            int    lo = (int)Math.Floor(t);
            int    hi = (int)Math.Ceiling(t);

            // Clamp for boundary safety (extrapolation beyond boundaries
            // is handled naturally since lo/hi can equal 0 or srcLen-1)
            lo = Math.Max(0, Math.Min(srcLen - 1, lo));
            hi = Math.Max(0, Math.Min(srcLen - 1, hi));

            double frac = t - lo;

            for (int p = 0; p < numPhonemes; p++)
            {
                output[n, p] = (float)(ppg[lo, p] * (1.0 - frac) + ppg[hi, p] * frac);
            }
        }

        return output;
    }
}
