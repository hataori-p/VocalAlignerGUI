using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Frontend.Core.Inference;

public class RefinerEngine : OnnxModelBase
{
    // --- Public state ---
    public bool IsAvailable { get; private set; } = false;

    // --- Validated dims from meta.yaml ---
    public int FeatureDim  { get; private set; } = 41;
    public int TimeSteps   { get; private set; } = 24;
    public int NumPhonemes { get; private set; } = 40;

    // --- Input/output names (must match export_refiner.py) ---
    private const string InputFeatures   = "features";
    private const string InputLeftId     = "left_phoneme_ids";
    private const string InputRightId    = "right_phoneme_ids";
    private const string OutputOffsetMs  = "offset_ms";

    public RefinerEngine(string modelPath)
    {
        string metaPath = Path.ChangeExtension(modelPath, ".yaml");

        // --- Validate meta.yaml ---
        if (!File.Exists(metaPath))
        {
            Console.WriteLine($"[RefinerEngine] WARNING: Meta file not found: {metaPath}");
            Console.WriteLine($"[RefinerEngine] Refinement stage will be DISABLED (zero offsets).");
            return;
        }

        if (!File.Exists(modelPath))
        {
            Console.WriteLine($"[RefinerEngine] WARNING: Model file not found: {modelPath}");
            Console.WriteLine($"[RefinerEngine] Refinement stage will be DISABLED (zero offsets).");
            return;
        }

        try
        {
            LoadMeta(metaPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RefinerEngine] WARNING: Failed to parse meta.yaml: {ex.Message}");
            Console.WriteLine($"[RefinerEngine] Refinement stage will be DISABLED (zero offsets).");
            return;
        }

        try
        {
            LoadSession(modelPath);
            IsAvailable = true;
            Console.WriteLine($"[RefinerEngine] Loaded successfully.");
            Console.WriteLine($"[RefinerEngine]   feature_dim  : {FeatureDim}");
            Console.WriteLine($"[RefinerEngine]   time_steps   : {TimeSteps}");
            Console.WriteLine($"[RefinerEngine]   num_phonemes : {NumPhonemes}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RefinerEngine] WARNING: Failed to load ONNX session: {ex.Message}");
            Console.WriteLine($"[RefinerEngine] Refinement stage will be DISABLED (zero offsets).");
        }
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Refines a single boundary.
    /// Returns the time offset in milliseconds to add to the boundary time.
    /// Returns 0.0f if the refiner is unavailable.
    /// </summary>
    public float Refine(float[,] features, int leftPhonemeId, int rightPhonemeId)
    {
        if (!IsAvailable) return 0.0f;

        var results = RefineBatch(
            new[] { features },
            new[] { leftPhonemeId },
            new[] { rightPhonemeId }
        );
        return results[0];
    }

    /// <summary>
    /// Refines a batch of boundaries in a single ONNX call.
    /// Returns array of offsets in milliseconds, one per boundary.
    /// Returns array of zeros if the refiner is unavailable.
    /// </summary>
    public float[] RefineBatch(float[][,] featuresBatch, int[] leftIds, int[] rightIds)
    {
        int B = featuresBatch.Length;

        if (!IsAvailable)
            return new float[B];

        if (_session == null)
        {
            Console.WriteLine("[RefinerEngine] ERROR: Session is null despite IsAvailable=true. Returning zeros.");
            return new float[B];
        }

        if (leftIds.Length != B || rightIds.Length != B)
            throw new ArgumentException($"[RefinerEngine] Batch size mismatch: features={B}, leftIds={leftIds.Length}, rightIds={rightIds.Length}");

        try
        {
            // --- Build features tensor: (B, TimeSteps, FeatureDim) ---
            var featTensor = new DenseTensor<float>(new[] { B, TimeSteps, FeatureDim });
            for (int b = 0; b < B; b++)
            {
                var feat = featuresBatch[b];
                if (feat.GetLength(0) != TimeSteps || feat.GetLength(1) != FeatureDim)
                    throw new ArgumentException(
                        $"[RefinerEngine] Feature window [{b}] has shape ({feat.GetLength(0)}, {feat.GetLength(1)}), " +
                        $"expected ({TimeSteps}, {FeatureDim})");

                for (int t = 0; t < TimeSteps; t++)
                    for (int f = 0; f < FeatureDim; f++)
                        featTensor[b, t, f] = feat[t, f];
            }

            // --- Build left/right phoneme id tensors: (B,) int64 ---
            var leftTensor  = new DenseTensor<long>(new[] { B });
            var rightTensor = new DenseTensor<long>(new[] { B });
            for (int b = 0; b < B; b++)
            {
                leftTensor[b]  = leftIds[b];
                rightTensor[b] = rightIds[b];
            }

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(InputFeatures,  featTensor),
                NamedOnnxValue.CreateFromTensor(InputLeftId,    leftTensor),
                NamedOnnxValue.CreateFromTensor(InputRightId,   rightTensor),
            };

            using var results = _session.Run(inputs);

            // --- Extract output: (B, 1) float32 ---
            var outputTensor = results.First().AsTensor<float>();
            var offsets = new float[B];
            for (int b = 0; b < B; b++)
                offsets[b] = outputTensor[b, 0];

            return offsets;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RefinerEngine] ERROR during inference: {ex.Message}");
            Console.WriteLine($"[RefinerEngine] Returning zero offsets for this batch.");
            return new float[B];
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void LoadMeta(string metaPath)
    {
        // Minimal YAML parser — reads "key: value" lines, no dependencies
        var lines = File.ReadAllLines(metaPath);
        var dict  = new Dictionary<string, string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
            var idx = trimmed.IndexOf(':');
            if (idx < 0) continue;
            var key = trimmed[..idx].Trim();
            var val = trimmed[(idx + 1)..].Trim();
            dict[key] = val;
        }

        // Validate and assign dims
        FeatureDim  = ParseRequiredInt(dict, "feature_dim",   metaPath);
        TimeSteps   = ParseRequiredInt(dict, "time_steps",    metaPath);
        NumPhonemes = ParseRequiredInt(dict, "num_phonemes",  metaPath);

        // Sanity check
        int expectedFeatureDim = ParseRequiredInt(dict, "ppg_dim", metaPath)
                               + ParseRequiredInt(dict, "flux_dim", metaPath);
        if (FeatureDim != expectedFeatureDim)
            throw new InvalidDataException(
                $"[RefinerEngine] Meta inconsistency: feature_dim={FeatureDim} " +
                $"but ppg_dim+flux_dim={expectedFeatureDim}");

        string unit = dict.TryGetValue("output_unit", out var u) ? u : "unknown";
        if (!string.Equals(unit, "milliseconds", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine($"[RefinerEngine] WARNING: output_unit='{unit}', expected 'milliseconds'. Offsets may be wrong.");
    }

    private static int ParseRequiredInt(Dictionary<string, string> dict, string key, string path)
    {
        if (!dict.TryGetValue(key, out var val))
            throw new KeyNotFoundException($"[RefinerEngine] Required key '{key}' missing in {path}");
        if (!int.TryParse(val, out int result))
            throw new FormatException($"[RefinerEngine] Key '{key}' value '{val}' is not an integer in {path}");
        return result;
    }
}
