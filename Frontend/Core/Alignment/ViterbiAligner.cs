using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Frontend.Core.Alignment;

public static class ViterbiAligner
{
    /// <summary>
    /// Aligns a specific chunk of logits to a specific sequence of tokens.
    /// Based on 'run_chunk_alignment' from allosaurus_rex/aligner/aligner.py.
    /// </summary>
    /// <returns>Int array where result[i] is the frame index where token i ENDS.</returns>
    public static int[] AlignChunk(
        float[,] logits,
        int[] tokenIndices,
        bool dumpDebugInfo = false,
        int startFrame = 0,
        int endFrame = -1)
    {
        if (endFrame < 0 || endFrame > logits.GetLength(0))
            endFrame = logits.GetLength(0);
        if (startFrame < 0) startFrame = 0;

        int numFrames = endFrame - startFrame;
        int numTokens = tokenIndices.Length;

        if (numTokens == 0) return Array.Empty<int>();

        // Python source check:
        // if num_frames < num_phonemes: raise ValueError
        if (numFrames < numTokens)
        {
            // Fallback: If chunk is too short, just linear distribute (prevents crash)
            return LinearDistribute(numTokens, numFrames, startFrame);
        }

        double[,] trellis = new double[numFrames, numTokens];
        int[,] backpointers = new int[numFrames, numTokens];

        // Init with -Inf
        for (int t = 0; t < numFrames; t++)
            for (int j = 0; j < numTokens; j++)
                trellis[t, j] = double.NegativeInfinity;

        // Initialization at t=0
        // The first frame MUST align to the first token
        trellis[0, 0] = logits[startFrame + 0, tokenIndices[0]];

        // Fill Trellis
        for (int t = 1; t < numFrames; t++)
        {
            for (int j = 0; j < numTokens; j++)
            {
                int tokenId = tokenIndices[j];
                double emission = logits[startFrame + t, tokenId];

                // Previous score if we stay in same token (j)
                double prevSame = trellis[t - 1, j];
                
                // Previous score if we came from previous token (j-1)
                double prevDiff = (j > 0) ? trellis[t - 1, j - 1] : double.NegativeInfinity;

                if (prevSame >= prevDiff)
                {
                    trellis[t, j] = emission + prevSame;
                    backpointers[t, j] = j; // 0 means stayed
                }
                else
                {
                    trellis[t, j] = emission + prevDiff;
                    backpointers[t, j] = j - 1; // -1 means moved from prev
                }
            }
        }

        // --- DEBUG DUMP: TRELLIS ---
        if (dumpDebugInfo)
        {
            try
            {
                string tPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_native_trellis.bin");
                using (var fs = new System.IO.FileStream(tPath, System.IO.FileMode.Create))
                using (var bw = new System.IO.BinaryWriter(fs))
                {
                    int tf = trellis.GetLength(0);
                    int tt = trellis.GetLength(1);
                    bw.Write(tf);
                    bw.Write(tt);
                    for (int t = 0; t < tf; t++)
                    {
                        for (int j = 0; j < tt; j++)
                        {
                            bw.Write((float)trellis[t, j]);
                        }
                    }
                }
            }
            catch { }
        }
        // ----------------------------------------------

        // Backtracking
        // Must end at the last token (numTokens - 1)
        int[] tokenEndFrames = new int[numTokens];
        int currentTokenIdx = numTokens - 1;

        // Check validity of end state
        if (double.IsNegativeInfinity(trellis[numFrames - 1, currentTokenIdx]))
        {
             // Path broken (constraints too tight or audio too silent). Fallback to linear.
             System.Diagnostics.Debug.WriteLine("[Viterbi] Path broken: End state unreachable.");
             return LinearDistribute(numTokens, numFrames);
        }

        // Traverse backwards from last frame
        for (int t = numFrames - 1; t >= 0; t--)
        {
            // We want to record the frame where we *leave* a token state roughly,
            // or rather the span of frames assigned to a token.
            // In this specific implementation, we track transition points.
            
            // If the backpointer says we came from 'prev' (j-1), 
            // then frame 't' was the FIRST frame of the current token 'j'.
            // Therefore, frame 't-1' was the LAST frame of 'j-1'.
            
            int prevTokenIdx = backpointers[t, currentTokenIdx];
            
            if (prevTokenIdx != currentTokenIdx)
            {
                // We transitioned from prevTokenIdx -> currentTokenIdx at frame t.
                // So prevTokenIdx ended at t-1.
                if (prevTokenIdx >= 0)
                {
                    tokenEndFrames[prevTokenIdx] = t - 1;
                }
                currentTokenIdx = prevTokenIdx;
            }
        }
        
        // The last token always ends at the last frame of the chunk
        tokenEndFrames[numTokens - 1] = numFrames - 1;

        for (int i = 0; i < numTokens; i++)
            tokenEndFrames[i] += startFrame;

        // Fill any gaps (if a token was skipped or duration 0, though logic above prevents standard skipping)
        // However, standard Viterbi ensures connectivity. 
        // Just verify monotonic in case of weird floats.
        for (int i = 0; i < numTokens - 1; i++) {
            if (tokenEndFrames[i] < 0) tokenEndFrames[i] = i; // simple patch
            if (tokenEndFrames[i] >= tokenEndFrames[i+1]) tokenEndFrames[i] = tokenEndFrames[i+1] - 1;
        }

        return tokenEndFrames;
    }

    internal static void ApplyLogSoftmax(float[,] logits)
    {
        int frames = logits.GetLength(0);
        int classes = logits.GetLength(1);

        Parallel.For(0, frames, t =>
        {
            float max = float.MinValue;
            for (int c = 0; c < classes; c++) max = Math.Max(max, logits[t, c]);

            float sum = 0;
            for (int c = 0; c < classes; c++) sum += MathF.Exp(logits[t, c] - max);

            float logSum = MathF.Log(sum);
            for (int c = 0; c < classes; c++)
            {
                logits[t, c] = (logits[t, c] - max) - logSum;
            }
        });
    }

    private static int[] LinearDistribute(int numTokens, int numFrames, int startFrame = 0)
    {
        int[] boundaries = new int[numTokens];
        double step = (double)numFrames / numTokens;
        for (int i = 0; i < numTokens; i++)
        {
            boundaries[i] = startFrame + (int)((i + 1) * step) - 1;
        }
        return boundaries;
    }
}
