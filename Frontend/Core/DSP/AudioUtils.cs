using System;

namespace Frontend.Core.DSP;

public static class AudioUtils
{
    /// <summary>
    /// Resamples audio using a Windowed Sinc Interpolator (Band-limited).
    /// This prevents aliasing when downsampling and provides high-fidelity upsampling.
    /// </summary>
    public static float[] Resample(float[] input, int sourceRate, int targetRate)
    {
        if (sourceRate == targetRate) return input;
        if (input.Length == 0) return Array.Empty<float>();

        long outputLength = (long)input.Length * targetRate / sourceRate;
        float[] output = new float[outputLength];

        double ratio = (double)sourceRate / targetRate;
        
        // Kernel width (higher = better quality, slower). 
        // 32 zero-crossings (64 taps) is standard for high quality audio.
        int kernelWidth = 64; 

        // If downsampling, we must scale the kernel to act as a Low-Pass Filter
        // to cut off frequencies above the new Nyquist limit.
        double filterScale = Math.Min(1.0, (double)targetRate / sourceRate);
        
        // Effective filter size increases when downsampling
        int filterRadius = (int)Math.Ceiling(kernelWidth / filterScale); 

        // Parallelize for performance, as convolution is expensive
        System.Threading.Tasks.Parallel.For(0, output.Length, n =>
        {
            double centerSrcTime = n * ratio;
            int centerIdx = (int)centerSrcTime;
            
            double sum = 0.0;

            // Convolve with the Sinc kernel
            int start = Math.Max(0, centerIdx - filterRadius);
            int end = Math.Min(input.Length - 1, centerIdx + filterRadius);

            for (int i = start; i <= end; i++)
            {
                // Distance from center in input samples
                double dist = i - centerSrcTime;
                
                // Scale distance for the filter cutoff
                double x = dist * filterScale;

                // Sinc * Window (Blackman)
                double weight = Sinc(x) * BlackmanWindow(x / kernelWidth);
                
                // Scale amplitude for downsampling to maintain energy density
                weight *= filterScale;

                sum += input[i] * weight;
                // weightSum isn't strictly needed for sinc reconstruction if normalized correctly, 
                // but can be used for boundary normalization if desired. 
                // Here we rely on the filterScale.
            }

            output[n] = (float)sum;
        });

        return output;
    }

    public static float[] StereoToMono(float[] input)
    {
        if (input.Length % 2 != 0) return input; // Safety check

        float[] mono = new float[input.Length / 2];
        for (int i = 0; i < mono.Length; i++)
        {
            mono[i] = (input[i * 2] + input[i * 2 + 1]) * 0.5f;
        }
        return mono;
    }

    // Normalized Sinc function: sin(pi * x) / (pi * x)
    private static double Sinc(double x)
    {
        if (Math.Abs(x) < 1e-9) return 1.0;
        double pix = Math.PI * x;
        return Math.Sin(pix) / pix;
    }

    // Blackman Window function to reduce spectral leakage
    // x is normalized position in range [-1, 1] relative to window width
    private static double BlackmanWindow(double x)
    {
        if (Math.Abs(x) > 1.0) return 0.0;
        // Standard Blackman coefficients
        return 0.42 + 0.5 * Math.Cos(Math.PI * x) + 0.08 * Math.Cos(2.0 * Math.PI * x);
    }
}
