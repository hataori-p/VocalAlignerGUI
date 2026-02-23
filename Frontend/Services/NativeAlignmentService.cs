using Frontend.Core.Alignment;
using Frontend.Core.DSP;
using Frontend.Core.Inference;
using Frontend.Models;
using Frontend.Services.Scripting;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Frontend.Services;

/// <summary>
/// Central location for all native model file paths.
/// </summary>
public static class ModelPaths
{
    private static string Models => Path.Combine(AppContext.BaseDirectory, "resources", "models");

    public static string RexModel    => Path.Combine(Models, "rex_model.onnx");
    public static string RexRefiner  => Path.Combine(Models, "rex_refiner.onnx");
}

public class NativeAlignmentService
{
    private RexEngine? _engine;
    private RefinerEngine? _refiner;
    private IModelProfile? _loadedProfile;

    private string?   _cachedAudioPath;
    private float[]?  _cachedAudio16k;
    private float[,]? _cachedLogits;
    private float[,]? _cachedLogSoftmax;
    private float[,]? _cachedPpg;

    public bool IsAvailable => _engine != null;

    public Action? OnAvailabilityChanged { get; set; }

    /// <summary>
    /// Converts raw logits (T, 40) to softmax PPG (T, 36).
    /// Skips the first 4 special tokens (<s>, <pad>, </s>, <unk>) at indices 0-3.
    /// Matches generate_ppg.py: index_select(real_phoneme_indices) then softmax.
    /// </summary>
    private static float[,] ComputePpg(float[,] logits, int ppgDim)
    {
        int T          = logits.GetLength(0);
        int totalCols  = logits.GetLength(1);  // 40
        int specialOff = totalCols - ppgDim;   // 4 — number of special tokens to skip

        float[,] ppg = new float[T, ppgDim];

        for (int t = 0; t < T; t++)
        {
            // Softmax over columns [specialOff .. totalCols-1] only (indices 4..39)
            float maxVal = float.NegativeInfinity;
            for (int c = 0; c < ppgDim; c++)
            {
                float v = logits[t, specialOff + c];
                if (v > maxVal) maxVal = v;
            }

            float sum = 0f;
            for (int c = 0; c < ppgDim; c++)
                sum += MathF.Exp(logits[t, specialOff + c] - maxVal);

            for (int c = 0; c < ppgDim; c++)
                ppg[t, c] = MathF.Exp(logits[t, specialOff + c] - maxVal) / sum;
        }

        return ppg;
    }

    public async Task LoadModelAsync(IModelProfile profile)
    {
        UnloadModel();

        // Manual-mode profiles have no ONNX model — store profile only
        if (!string.IsNullOrEmpty(profile.ModelFile))
        {
            string modelPath = Path.Combine(AppContext.BaseDirectory, profile.ModelFile);
            if (!File.Exists(modelPath))
            {
                Console.WriteLine($"[NativeAligner] Model not found: {modelPath}");
                return;
            }

            await Task.Run(() => _engine = new RexEngine(modelPath));

            string? refinerPath = profile.RefinerFile != null
                ? Path.Combine(AppContext.BaseDirectory, profile.RefinerFile)
                : null;

            _refiner = new RefinerEngine(refinerPath ?? string.Empty);
        }
        else
        {
            Console.WriteLine($"[NativeAligner] Manual profile — no ONNX model to load.");
        }

        _loadedProfile = profile;
        InvalidateCache();
        Console.WriteLine($"[NativeAligner] Loaded: {profile.DisplayName}");
        OnAvailabilityChanged?.Invoke();
    }

    public void UnloadModel()
    {
        _engine?.Dispose();
        _engine = null;
        _refiner?.Dispose();
        _refiner = null;
        _loadedProfile = null;
        InvalidateCache();
        OnAvailabilityChanged?.Invoke();
    }

    public void InvalidateCache()
    {
        _cachedAudioPath   = null;
        _cachedAudio16k    = null;
        _cachedLogits      = null;
        _cachedLogSoftmax  = null;
        _cachedPpg         = null;
        Console.WriteLine("[Cache] Invalidated.");
    }

    private async Task EnsureAudioCacheAsync(string audioPath)
    {
        if (_cachedAudioPath == audioPath && _cachedLogits != null)
            return;

        await Task.Run(() =>
        {
            // --- LOAD & RESAMPLE ---
            float[] audio16k;
            using (var reader = new AudioFileReader(audioPath))
            {
                ISampleProvider provider = reader;
                if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1)
                    provider = new WdlResamplingSampleProvider(provider, 16000);

                var buffer = new List<float>();
                var readBuffer = new float[16000];
                int read;
                while ((read = provider.Read(readBuffer, 0, readBuffer.Length)) > 0)
                {
                    if (provider.WaveFormat.Channels > 1)
                    {
                        for (int i = 0; i < read; i += provider.WaveFormat.Channels)
                        {
                            float sum = 0;
                            for (int c = 0; c < provider.WaveFormat.Channels; c++) sum += readBuffer[i + c];
                            buffer.Add(sum / provider.WaveFormat.Channels);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < read; i++) buffer.Add(readBuffer[i]);
                    }
                }
                audio16k = buffer.ToArray();
            }

            // --- PADDING ---
            int pad = 200;
            float[] paddedAudio = new float[audio16k.Length + pad * 2];
            Array.Copy(audio16k, 0, paddedAudio, pad, audio16k.Length);

            // --- HIJACK ---
            string? hijackEnabled = Environment.GetEnvironmentVariable("VOCALALIGNER_HIJACK_INPUT");
            if (hijackEnabled == "1")
            {
                string backendDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Backend"));
                string pyInputPath = Path.Combine(backendDir, "debug_python_input.bin");
                if (File.Exists(pyInputPath))
                {
                    try
                    {
                        using var fs = File.OpenRead(pyInputPath);
                        using var br = new BinaryReader(fs);
                        int rows = br.ReadInt32();
                        int cols = br.ReadInt32();
                        float[] pyAudio = new float[cols];
                        for (int i = 0; i < cols; i++) pyAudio[i] = br.ReadSingle();
                        paddedAudio = pyAudio;
                        audio16k = new float[cols - pad * 2];
                        Array.Copy(paddedAudio, pad, audio16k, 0, audio16k.Length);
                        Console.WriteLine($"[NativeAligner] HIJACK ACTIVE: {cols} padded samples");
                    }
                    catch (Exception ex) { Console.WriteLine($"[NativeAligner] Hijack failed: {ex.Message}"); }
                }
            }

            // --- INFERENCE ---
            if (_engine == null)
                throw new InvalidOperationException("[NativeAligner] No model loaded. Select a model from the dropdown first.");

            int sr = 16000;
            int chunkSize = 30 * sr;
            int contextPad = 200;
            int rawLen = audio16k.Length;
            int numChunks = (int)Math.Ceiling((double)rawLen / chunkSize);

            var logitChunks = new List<float[]>();
            int numClasses = 0;
            int totalProcessed = 0;
            int totalTaken = 0;

            for (int i = 0; i < numChunks; i++)
            {
                int start = i * chunkSize;
                int validLen = Math.Min(chunkSize, rawLen - start);
                if (validLen <= 0) break;

                int chunkLen = validLen + 2 * contextPad;
                float[] chunkBuffer = new float[chunkLen];
                int extractLen = Math.Min(chunkLen, paddedAudio.Length - start);
                if (extractLen > 0)
                    Array.Copy(paddedAudio, start, chunkBuffer, 0, extractLen);

                var outcomes = _engine.Forward(chunkBuffer);
                int frames = outcomes.GetLength(0);
                numClasses = outcomes.GetLength(1);

                totalProcessed += validLen;
                int expectedTotal = GetFeatExtractOutputLengths(totalProcessed);
                int take = Math.Max(0, Math.Min(expectedTotal - totalTaken, frames));

                if (take > 0)
                {
                    float[] chunk = new float[take * numClasses];
                    for (int t = 0; t < take; t++)
                        for (int c = 0; c < numClasses; c++)
                            chunk[t * numClasses + c] = outcomes[t, c];
                    logitChunks.Add(chunk);
                }
                totalTaken += take;
            }

            // Reassemble logits
            int totalFrames = logitChunks.Sum(x => x.Length / numClasses);
            var logits = new float[totalFrames, numClasses];
            int writeRow = 0;
            foreach (var part in logitChunks)
            {
                int rows = part.Length / numClasses;
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < numClasses; c++)
                        logits[writeRow + r, c] = part[r * numClasses + c];
                writeRow += rows;
            }

            // Compute PPG
            int ppgDim = RefinementFeatureExtractor.PpgDim;
            var ppg = ComputePpg(logits, ppgDim);

            // Apply LogSoftmax once and cache — AlignChunk must NOT mutate this
            var logSoftmax = (float[,])logits.Clone();
            ViterbiAligner.ApplyLogSoftmax(logSoftmax);

            Console.WriteLine($"[Cache] Stored: audio={audio16k.Length} samples, logits=({totalFrames},{numClasses}), ppg=({ppg.GetLength(0)},{ppgDim})");

            // Store in cache
            _cachedAudioPath  = audioPath;
            _cachedAudio16k   = audio16k;
            _cachedLogits     = logits;
            _cachedLogSoftmax = logSoftmax;
            _cachedPpg        = ppg;
        });
    }

    private int GetFeatExtractOutputLengths(long inputLen)
    {
        long l = inputLen;
        l = (long)Math.Floor((l - 10) / 5.0) + 1; // (512,10,5)
        for (int i = 0; i < 4; i++)
            l = (long)Math.Floor((l - 3) / 2.0) + 1; // 4x (512,3,2)
        for (int i = 0; i < 2; i++)
            l = (long)Math.Floor((l - 2) / 2.0) + 1; // 2x (512,2,2)
        return (int)l;
    }

    public async Task<AlignResponse?> AlignAsync(string audioPath, string text, List<AlignmentConstraint>? constraints)
    {
        await EnsureAudioCacheAsync(audioPath);

        return await Task.Run(() =>
        {
            try
            {
                Console.WriteLine($"\n[NativeAligner] === AlignAsync START (cache hit) ===");

                float[] audio16k = _cachedAudio16k!;
                float[,] logits  = _cachedLogSoftmax!;
                float[,] rawPpg  = _cachedPpg!;

                int totalFrames = logits.GetLength(0);

                // --- 3. Prepare Tokens ---
                // The ViewModel passes 'text' as a space-joined string of ALL tokens in the grid.
                var rawTokens = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                
                List<int> allTokenIds = new();
                List<string> cleanTokens = new();
                var vocab = _engine!.Vocab;
                int silId = vocab.ContainsKey("sil") ? vocab["sil"] : 0;

                foreach (var t in rawTokens)
                {
                    // Map "_" -> "sil" for alignment ID, but keep original text for result
                    string lookup = (t == "_") ? "sil" : t;
                    
                    if (vocab.ContainsKey(lookup))
                        allTokenIds.Add(vocab[lookup]);
                    else
                        allTokenIds.Add(vocab.ContainsKey("<unk>") ? vocab["<unk>"] : 0);
                    
                    cleanTokens.Add(t); 
                }

                // --- 4. Process Constrained Chunks ---
                // We define segments based on constraints.
                // A constraint is (Time, TokenIndexInclusive). 
                // We imply a start constraint at (0.0, 0).
                
                var segmentResults = new List<AlignmentInterval>();
                
                double prevTime = 0.0;
                int prevTokenIdx = 0;
                
                // Ensure we have a final constraint for the end of file
                var effectiveConstraints = new List<AlignmentConstraint>();
                if (constraints != null) effectiveConstraints.AddRange(constraints);
                
                // Add implicit end constraint if missing
                double audioDuration = audio16k.Length / 16000.0;
                if (effectiveConstraints.Count == 0 || effectiveConstraints.Last().PhonemeIndex < allTokenIds.Count)
                {
                    effectiveConstraints.Add(new AlignmentConstraint(audioDuration, allTokenIds.Count));
                }
                
                double frameDuration = 320.0 / 16000.0; // 0.02s
                
                // Track if this is the first chunk for debug dumping
                bool isFirstChunk = true;

                foreach (var constraint in effectiveConstraints.OrderBy(c => c.PhonemeIndex))
                {
                    double currTime = constraint.Time;
                    int currTokenIdx = constraint.PhonemeIndex; // Exclusive upper bound for Range, Inclusive for Constraint logic usually?
                    // VM Logic: "phonemeCounter += tokens.Length; constraints.Add(..., phonemeCounter)"
                    // So if tokens are [0,1,2], counter is 3. Constraint is 3. Range is 0..3 (length 3). Correct.

                    int tokenCount = currTokenIdx - prevTokenIdx;
                    if (tokenCount <= 0) continue; // Skip if no tokens in this span

                    // Convert Time to Frames
                    int startFrame = (int)(prevTime / frameDuration);
                    int endFrame = (int)(currTime / frameDuration);

                    // Clamp
                    if (startFrame < 0) startFrame = 0;
                    if (endFrame > totalFrames) endFrame = totalFrames;
                    
                    int frameCount = endFrame - startFrame;

                    System.Diagnostics.Debug.WriteLine($"[NativeAligner] Chunk: Tokens {prevTokenIdx}-{currTokenIdx} ({tokenCount}), Time {prevTime:F2}-{currTime:F2}s, Frames {startFrame}-{endFrame}");

                    if (frameCount > 0)
                    {
                        // Slice Logits
                        float[,] logitsChunk = new float[frameCount, logits.GetLength(1)];
                        for (int t = 0; t < frameCount; t++)
                        {
                            for (int c = 0; c < logits.GetLength(1); c++)
                            {
                                logitsChunk[t, c] = logits[startFrame + t, c];
                            }
                        }

                        // Slice Tokens
                        int[] tokensChunk = allTokenIds.GetRange(prevTokenIdx, tokenCount).ToArray();

                        // ALIGN CHUNK
                        int[] chunkBoundaries = ViterbiAligner.AlignChunk(logits, tokensChunk, isFirstChunk, startFrame, endFrame);
                        isFirstChunk = false;

                        // --- Phase 1: Collect raw boundaries as absolute times ---
                        // boundaries[0]   = prevTime  (start of chunk)
                        // boundaries[k+1] = end of phoneme k
                        // boundaries[N]   = currTime  (end of chunk, from constraint)
                        int N = chunkBoundaries.Length; // == tokenCount
                        double[] boundaries = new double[N + 1];
                        boundaries[0] = prevTime;
                        for (int k = 0; k < N; k++)
                            boundaries[k + 1] = (chunkBoundaries[k] + 1) * frameDuration;
                        // Force last boundary to match constraint exactly
                        boundaries[N] = currTime;

                        // --- Phase 2: Extract features + collect batch for refinement ---
                        // Only refine internal boundaries (1..N-1), not the chunk start/end
                        int internalCount = N - 1;
                        bool doRefine = (_refiner != null) && _refiner.IsAvailable && internalCount > 0;

                        float[][,]? featureBatch = doRefine ? new float[internalCount][,] : null;
                        int[]? leftIds           = doRefine ? new int[internalCount] : null;
                        int[]? rightIds          = doRefine ? new int[internalCount] : null;

                        if (doRefine)
                        {
                            for (int k = 0; k < internalCount; k++)
                            {
                                double boundaryTime = boundaries[k + 1]; // internal boundary
                                featureBatch![k]    = RefinementFeatureExtractor.Extract(
                                                          audio16k, rawPpg, boundaryTime);
                                leftIds![k]  = allTokenIds[prevTokenIdx + k];
                                rightIds![k] = allTokenIds[prevTokenIdx + k + 1];
                            }
                        }

                        // --- Phase 3: Batch refine + apply offsets with min-duration clamping ---
                        const double MinPhonemeSeconds = 0.010; // 10ms

                        if (doRefine)
                        {
                            float[] offsets = _refiner!.RefineBatch(featureBatch!, leftIds!, rightIds!);

                            // Keep original boundaries for upper-bound clamping (prevents cascade)
                            double[] originalBoundaries = (double[])boundaries.Clone();

                            for (int k = 0; k < internalCount; k++)
                            {
                                int bIdx = k + 1; // index into boundaries[]

                                double lowerBound = boundaries[bIdx - 1]          + MinPhonemeSeconds;
                                double upperBound = originalBoundaries[bIdx + 1]  - MinPhonemeSeconds;

                                if (lowerBound >= upperBound)
                                {
                                    // Not enough room — skip refinement for this boundary
                                    Console.WriteLine($"[NativeAligner] Boundary {bIdx}: skipped refinement " +
                                                      $"(no room: lower={lowerBound:F4} >= upper={upperBound:F4})");
                                    continue;
                                }

                                double refined = boundaries[bIdx] + offsets[k] / 1000.0;
                                double clamped = Math.Max(lowerBound, Math.Min(upperBound, refined));

                                Console.WriteLine($"[NativeAligner] Boundary {bIdx}: " +
                                                  $"raw={boundaries[bIdx]:F4}s  " +
                                                  $"offset={offsets[k]:F2}ms  " +
                                                  $"refined={refined:F4}s  " +
                                                  $"clamped={clamped:F4}s");

                                boundaries[bIdx] = clamped;
                            }
                        }

                        // --- Phase 4: Construct intervals from refined boundaries ---
                        for (int k = 0; k < N; k++)
                        {
                            segmentResults.Add(new AlignmentInterval(
                                boundaries[k],
                                boundaries[k + 1],
                                cleanTokens[prevTokenIdx + k]));
                        }
                    }
                    else 
                    {
                         // Zero duration chunk (user error or extremely tight constraint).
                         // Fill with zero dur intervals.
                         for(int k=0; k < tokenCount; k++)
                             segmentResults.Add(new AlignmentInterval(prevTime, prevTime, cleanTokens[prevTokenIdx + k]));
                    }

                    prevTime = currTime;
                    prevTokenIdx = currTokenIdx;
                }

                return new AlignResponse("success", segmentResults, constraints);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NativeAligner] CRITICAL ERROR: {ex}");
                throw;
            }
        });
    }

    /// <summary>
    /// Aligns exactly two adjacent intervals against a pre-sliced logit window.
    /// The cache must already be warm before calling this.
    /// Returns the refined time of each interior boundary (i.e. between tokens within each interval).
    /// The pivot boundary time (between the two intervals) is passed as a hard constraint.
    /// </summary>
    public async Task<List<double>?> AlignScopedAsync(
        string audioPath,
        double startTime,
        double pivotTime,
        double endTime,
        List<string> leftTokens,
        List<string> rightTokens)
    {
        // Cache must already be warm — EnsureAudioCacheAsync was called on audio load.
        // But guard against stale path just in case.
        await EnsureAudioCacheAsync(audioPath);

        return await Task.Run(() =>
        {
            try
            {
                var logits   = _cachedLogSoftmax!;
                var audio16k = _cachedAudio16k!;
                var rawPpg   = _cachedPpg!;
                var vocab    = _engine!.Vocab;

                double frameDuration = 320.0 / 16000.0;
                int totalFrames = logits.GetLength(0);

                // Convert wall-clock times to absolute frame indices in the cache
                int startFrameAbs = Math.Max(0, (int)(startTime / frameDuration));
                int pivotFrameAbs = Math.Max(0, Math.Min(totalFrames, (int)(pivotTime / frameDuration)));
                int endFrameAbs   = Math.Max(0, Math.Min(totalFrames, (int)(endTime   / frameDuration)));

                // Helper: map token string to vocab id
                int TokenId(string t)
                {
                    string lookup = t == "_" ? "sil" : t;
                    return vocab.TryGetValue(lookup, out int id) ? id
                        : (vocab.TryGetValue("<unk>", out int unk) ? unk : 0);
                }

                // --- Align left interval [startTime → pivotTime] ---
                int[] leftIds  = leftTokens.Select(TokenId).ToArray();
                int[] leftEndFrames = ViterbiAligner.AlignChunk(
                    logits, leftIds, false,
                    startFrame: startFrameAbs,
                    endFrame:   pivotFrameAbs);

                // Interior boundaries of left interval (absolute frame → wall-clock time)
                // leftEndFrames has leftTokens.Count entries; we want indices [0..Count-2]
                var leftInteriorTimes = new List<double>();
                for (int k = 0; k < leftTokens.Count - 1; k++)
                    leftInteriorTimes.Add((leftEndFrames[k] + 1) * frameDuration);

                // --- Align right interval [pivotTime → endTime] ---
                int[] rightIds = rightTokens.Select(TokenId).ToArray();
                int[] rightEndFrames = ViterbiAligner.AlignChunk(
                    logits, rightIds, false,
                    startFrame: pivotFrameAbs,
                    endFrame:   endFrameAbs);

                // Interior boundaries of right interval
                var rightInteriorTimes = new List<double>();
                for (int k = 0; k < rightTokens.Count - 1; k++)
                    rightInteriorTimes.Add((rightEndFrames[k] + 1) * frameDuration);

                // --- Refinement pass (mirrors AlignAsync Phase 2-3) ---
                var allInterior = new List<double>();
                allInterior.AddRange(leftInteriorTimes);
                allInterior.AddRange(rightInteriorTimes);

                bool doRefine = (_refiner != null) && _refiner.IsAvailable && allInterior.Count > 0;

                if (doRefine)
                {
                    // Build token id lists for left and right
                    int internalCount = allInterior.Count;
                    var featureBatch = new float[internalCount][,];
                    var batchLeftIds  = new int[internalCount];
                    var batchRightIds = new int[internalCount];

                    // Left interval interior boundaries: between leftTokens[k] and leftTokens[k+1]
                    for (int k = 0; k < leftInteriorTimes.Count; k++)
                    {
                        featureBatch[k] = RefinementFeatureExtractor.Extract(audio16k, rawPpg, allInterior[k]);
                        batchLeftIds[k]  = TokenId(leftTokens[k]);
                        batchRightIds[k] = TokenId(leftTokens[k + 1]);
                    }

                    // Right interval interior boundaries: between rightTokens[k] and rightTokens[k+1]
                    int offset = leftInteriorTimes.Count;
                    for (int k = 0; k < rightInteriorTimes.Count; k++)
                    {
                        featureBatch[offset + k] = RefinementFeatureExtractor.Extract(audio16k, rawPpg, allInterior[offset + k]);
                        batchLeftIds[offset + k]  = TokenId(rightTokens[k]);
                        batchRightIds[offset + k] = TokenId(rightTokens[k + 1]);
                    }

                    float[] offsets = _refiner!.RefineBatch(featureBatch, batchLeftIds, batchRightIds);

                    const double MinPhonemeSeconds = 0.010;

                    // Apply offsets with min-duration clamping
                    // Build a flat boundary list including the fixed anchors for clamping reference:
                    // [ startTime, ...leftInterior..., pivotTime, ...rightInterior..., endTime ]
                    var allBoundaries = new List<double>();
                    allBoundaries.Add(startTime);
                    allBoundaries.AddRange(leftInteriorTimes);
                    allBoundaries.Add(pivotTime);
                    allBoundaries.AddRange(rightInteriorTimes);
                    allBoundaries.Add(endTime);

                    // Interior indices in allBoundaries are 1..leftCount and (leftCount+1)..(leftCount+rightCount)
                    // Pivot at index leftCount+1 is NOT refined (it is a locked anchor)
                    // Map allInterior[i] → allBoundaries[i < leftCount ? i+1 : i+2]
                    for (int k = 0; k < internalCount; k++)
                    {
                        // Index in allBoundaries: left interiors are at [1..leftCount], right at [leftCount+2..leftCount+1+rightCount]
                        int bIdx = k < leftInteriorTimes.Count
                            ? k + 1
                            : k + 2; // skip pivot at leftCount+1

                        double lowerBound = allBoundaries[bIdx - 1] + MinPhonemeSeconds;
                        double upperBound = allBoundaries[bIdx + 1] - MinPhonemeSeconds;

                        if (lowerBound >= upperBound)
                        {
                            Console.WriteLine($"[AlignScopedAsync] Boundary {k}: skipped refinement (no room)");
                            continue;
                        }

                        double refined = allInterior[k] + offsets[k] / 1000.0;
                        double clamped = Math.Max(lowerBound, Math.Min(upperBound, refined));

                        Console.WriteLine($"[AlignScopedAsync] Boundary {k}: " +
                                          $"raw={allInterior[k]:F4}s  " +
                                          $"offset={offsets[k]:F2}ms  " +
                                          $"refined={refined:F4}s  " +
                                          $"clamped={clamped:F4}s");

                        allInterior[k] = clamped;
                    }
                }

                return allInterior;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AlignScopedAsync] Error: {ex}");
                return null;
            }
        });
    }

    public async Task<List<string>> RecognizeAsync(string audioPath)
    {
        await EnsureAudioCacheAsync(audioPath);

        return await Task.Run(() =>
        {
            var logits = _cachedLogits!;
            int totalFrames = logits.GetLength(0);
            int numClasses  = logits.GetLength(1);

            var phonemes = new List<string>();
            for (int t = 0; t < totalFrames; t++)
            {
                int bestClass = 0;
                float bestVal = float.NegativeInfinity;
                for (int c = 0; c < numClasses; c++)
                {
                    float val = logits[t, c];
                    if (val > bestVal) { bestVal = val; bestClass = c; }
                }
                phonemes.Add(_engine!.ReverseVocab.TryGetValue(bestClass, out var ph) ? ph : "sil");
            }
            return phonemes;
        });
    }
}
