using System;
using System.Linq;

namespace Frontend.Core.DSP;

/// <summary>
/// Extracts a (24, 41) feature window centered on a boundary time,
/// matching allosaurus_rex.refinement_model.features.extract_feature_window exactly.
/// Feature layout: [PPG(36) | SpectralFlux(5)] per time step.
/// </summary>
public static class RefinementFeatureExtractor
{
    public const int   AnalysisWindowMs   = 120;
    public const int   TargetRateHz       = 200;
    public const int   NativePpgRateHz    = 50;
    public const int   Sr                 = 16000;
    public const int   HopLength          = Sr / TargetRateHz;
    public const int   WinLength          = HopLength * 5;
    public const int   AudioWindowSamples = Sr * AnalysisWindowMs / 1000;
    public const int   TimeSteps          = AnalysisWindowMs * TargetRateHz / 1000;
    public const int   PpgDim             = 36;
    public const int   FluxDim            = 5;
    public const int   FeatureDim         = PpgDim + FluxDim;

    public static float[,] Extract(
        float[]  fullAudio,
        float[,] rawPpg50Hz,
        double   centerTimeS)
    {
        // 1. Audio window
        int centerSample = (int)Math.Round(centerTimeS * Sr);
        int halfSamples  = AudioWindowSamples / 2;
        int sStart       = centerSample - halfSamples;
        int sEnd         = sStart + AudioWindowSamples;
        float[] audioWindow = ReflectPadSlice(fullAudio, sStart, sEnd);

        // 2. STFT → Mel → Flux
        float[,] power = StftProcessor.ComputePowerSpectrum(audioWindow);
        float[,] mel   = MelFilterbank.Apply(power);
        float[,] flux  = SpectralFluxExtractor.Compute(mel);

        // 3. PPG window — slice at 50Hz first, then upsample the slice (matches Python)
        double halfWindowS = AnalysisWindowMs / 1000.0 / 2.0;
        int pStart50 = (int)((centerTimeS - halfWindowS) * NativePpgRateHz);
        int pEnd50   = (int)((centerTimeS + halfWindowS) * NativePpgRateHz);
        pStart50 = Math.Max(0, pStart50);
        pEnd50   = Math.Min(rawPpg50Hz.GetLength(0), pEnd50);

        // Slice the raw 50Hz window
        int sliceLen = pEnd50 - pStart50;
        float[,] ppgSlice = new float[sliceLen, PpgDim];
        for (int i = 0; i < sliceLen; i++)
            for (int j = 0; j < PpgDim; j++)
                ppgSlice[i, j] = rawPpg50Hz[pStart50 + i, j];

        // Upsample PPG slice using rate-based resampling matching Python upsample_ppgs:
        // output_len = round(sliceLen * TARGET_RATE / NATIVE_RATE)
        int upsampledLen    = (int)Math.Round((double)sliceLen * TargetRateHz / NativePpgRateHz);
        upsampledLen        = Math.Max(1, upsampledLen); // guard against zero
        float[,] ppgUpsampled = PpgUpsampler.Upsample(ppgSlice, upsampledLen);

        // 4. Compute minLen matching Python: min(upsampled_ppg_len, flux_len)
        int fluxFrames  = flux.GetLength(0);
        int minLen      = Math.Min(upsampledLen, fluxFrames);
        int actualSteps = Math.Min(minLen, TimeSteps);

        // 5. Assemble — zero-pad to TimeSteps if minLen < TimeSteps (matches Python)
        float[,] features = new float[TimeSteps, FeatureDim];
        for (int t = 0; t < TimeSteps; t++)
        {
            for (int p = 0; p < PpgDim; p++)
                features[t, p] = t < actualSteps ? ppgUpsampled[t, p] : 0f;
            for (int f = 0; f < FluxDim; f++)
                features[t, PpgDim + f] = t < actualSteps ? flux[t, f] : 0f;
        }

        return features;
    }

    public static float[] ReflectPadSlice(float[] src, int start, int end)
    {
        int len    = end - start;
        int srcLen = src.Length;
        float[] dst = new float[len];
        for (int i = 0; i < len; i++)
            dst[i] = src[ReflectIndex(start + i, srcLen)];
        return dst;
    }

    public static float[,] ReflectPadSlice2D(float[,] src, int rowStart, int rowEnd)
    {
        int rows   = rowEnd - rowStart;
        int cols   = src.GetLength(1);
        int srcLen = src.GetLength(0);
        float[,] dst = new float[rows, cols];
        for (int i = 0; i < rows; i++)
        {
            int idx = ReflectIndex(rowStart + i, srcLen);
            for (int j = 0; j < cols; j++)
                dst[i, j] = src[idx, j];
        }
        return dst;
    }

    public static int ReflectIndex(int idx, int length)
    {
        if (length == 1) return 0;
        int period = 2 * (length - 1);
        idx = ((idx % period) + period) % period;
        if (idx >= length) idx = period - idx;
        return idx;
    }
}
