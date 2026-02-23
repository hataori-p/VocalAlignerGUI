using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OnnxVerify2;

class Program
{
    // Paths relative to repo root — adjust if running from project dir
    static string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
    static string ModelPath = Path.Combine(RepoRoot, "Frontend", "resources", "models", "rex_model.onnx");
    static string InputPath = Path.Combine(RepoRoot, "Backend", "debug_python_chunk_input.bin");
    static string PythonLogitsPath = Path.Combine(RepoRoot, "Backend", "debug_python_raw_model_output.bin");

    static float[] ReferenceInput = Array.Empty<float>();
    static float[,] ReferenceLogits = new float[0, 0];

    static int Main(string[] args)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════╗");
        Console.WriteLine("║  OnnxVerify2 — Session Behavior Isolation Harness   ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // --- Resolve paths ---
        Console.WriteLine($"[SETUP] Repo root:    {RepoRoot}");
        Console.WriteLine($"[SETUP] Model:        {ModelPath}");
        Console.WriteLine($"[SETUP] Input:        {InputPath}");
        Console.WriteLine($"[SETUP] Python ref:   {PythonLogitsPath}");
        Console.WriteLine();

        // Report runtime info
        Console.WriteLine($"[SETUP] ORT Version: {OrtEnv.Instance().GetVersionString()}");
        foreach (System.Diagnostics.ProcessModule mod in System.Diagnostics.Process.GetCurrentProcess().Modules)
        {
            if (mod.ModuleName != null && mod.ModuleName.Contains("onnxruntime", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[SETUP] Native DLL: {mod.FileName} ({mod.ModuleMemorySize} bytes)");
            }
        }
        Console.WriteLine();

        if (!File.Exists(ModelPath)) { Console.WriteLine($"FATAL: Model not found: {ModelPath}"); return 1; }
        if (!File.Exists(InputPath)) { Console.WriteLine($"FATAL: Input not found: {InputPath}"); return 1; }
        if (!File.Exists(PythonLogitsPath)) { Console.WriteLine($"FATAL: Python logits not found: {PythonLogitsPath}"); return 1; }

        // --- Load reference data once ---
        Console.WriteLine("[SETUP] Loading reference input...");
        ReferenceInput = LoadBinMatrix1D(InputPath);
        Console.WriteLine($"        → {ReferenceInput.Length} samples");

        Console.WriteLine("[SETUP] Loading Python reference logits...");
        ReferenceLogits = LoadBinMatrix2D(PythonLogitsPath);
        Console.WriteLine($"        → {ReferenceLogits.GetLength(0)} frames × {ReferenceLogits.GetLength(1)} classes");
        Console.WriteLine();

        // --- Run tests ---
        int passed = 0;
        int failed = 0;

        void RunTest(string name, Func<float[,]> test)
        {
            Console.WriteLine($"┌─── TEST: {name} ───");
            try
            {
                var sw = Stopwatch.StartNew();
                float[,] logits = test();
                sw.Stop();
                Console.WriteLine($"│  Inference time: {sw.ElapsedMilliseconds}ms");
                Console.WriteLine($"│  Output: {logits.GetLength(0)} frames × {logits.GetLength(1)} classes");

                bool ok = CompareToReference(logits);
                if (ok) { Console.WriteLine($"└─── ✅ PASS: {name}"); passed++; }
                else    { Console.WriteLine($"└─── ❌ FAIL: {name}"); failed++; }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"│  EXCEPTION: {ex.Message}");
                Console.WriteLine($"└─── 💥 ERROR: {name}");
                failed++;
            }
            Console.WriteLine();
        }

        // ============================================================
        // TEST 1: Baseline — fresh session, immediate run, dispose
        // (This is what OnnxVerify does — MUST pass)
        // ============================================================
        RunTest("1. Baseline (fresh session, immediate run)", () =>
        {
            using var opts = new SessionOptions();
            opts.AppendExecutionProvider_CPU();
            using var session = new InferenceSession(ModelPath, opts);
            return RunInference(session, ReferenceInput);
        });

        // ============================================================
        // TEST 2: Session stored in field, not disposed between calls
        // ============================================================
        RunTest("2. Session as field (not disposed)", () =>
        {
            var holder = new SessionHolder(ModelPath);
            float[,] result = holder.Run(ReferenceInput);
            // Intentionally NOT disposing — simulates RexEngine lifetime
            return result;
        });

        // ============================================================
        // TEST 3: Temporal gap — create session, wait, then run
        // ============================================================
        RunTest("3. Temporal gap (create → 2s pause → run)", () =>
        {
            var holder = new SessionHolder(ModelPath);
            System.Threading.Thread.Sleep(2000);
            return holder.Run(ReferenceInput);
        });

        // ============================================================
        // TEST 4: GC pressure between creation and inference
        // ============================================================
        RunTest("4. GC pressure (alloc + collect between create and run)", () =>
        {
            var holder = new SessionHolder(ModelPath);

            // Simulate GC pressure
            for (int i = 0; i < 100; i++)
            {
                _ = new byte[1024 * 1024]; // 1MB allocations
            }
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);

            return holder.Run(ReferenceInput);
        });

        // ============================================================
        // TEST 5: Async context (simulates Avalonia command handler)
        // ============================================================
        RunTest("5. Async context (Task.Run → await)", () =>
        {
            var holder = new SessionHolder(ModelPath);
            var task = Task.Run(() => holder.Run(ReferenceInput));
            return task.GetAwaiter().GetResult();
        });

        // ============================================================
        // TEST 6: GC pressure between tensor creation and session.Run
        // ============================================================
        RunTest("6. GC pressure between tensor alloc and Run()", () =>
        {
            using var opts = new SessionOptions();
            opts.AppendExecutionProvider_CPU();
            using var session = new InferenceSession(ModelPath, opts);
            return RunInferenceWithGCPressure(session, ReferenceInput);
        });

        // ============================================================
        // TEST 7: Two sessions on same model, run second one
        // ============================================================
        RunTest("7. Double session (create two, run second)", () =>
        {
            var holder1 = new SessionHolder(ModelPath);
            var holder2 = new SessionHolder(ModelPath);
            _ = holder1.Run(ReferenceInput); // warm up first
            return holder2.Run(ReferenceInput); // test second
        });

        // ============================================================
        // TEST 8: Session reuse — run twice, compare second output
        // ============================================================
        RunTest("8. Session reuse (run twice, return second)", () =>
        {
            var holder = new SessionHolder(ModelPath);
            _ = holder.Run(ReferenceInput); // first call
            return holder.Run(ReferenceInput); // second call — is it still correct?
        });

        // ============================================================
        // TEST 9: Short input first, then long input (buffer reuse)
        // ============================================================
        RunTest("9. Short-then-long (16k samples → 480k samples)", () =>
        {
            var holder = new SessionHolder(ModelPath);
            float[] shortInput = new float[16000]; // 1 second of silence
            Array.Copy(ReferenceInput, shortInput, Math.Min(16000, ReferenceInput.Length));
            _ = holder.Run(shortInput); // prime with short
            return holder.Run(ReferenceInput); // then run real input
        });

        // ============================================================
        // SUMMARY
        // ============================================================
        Console.WriteLine("══════════════════════════════════════════════════════");
        Console.WriteLine($"  RESULTS:  {passed} passed,  {failed} failed");
        Console.WriteLine("══════════════════════════════════════════════════════");

        if (failed > 0)
            Console.WriteLine("\n⚠️  Some tests failed — the first failure identifies the trigger.");
        else
            Console.WriteLine("\n✅ All tests passed — the bug is Avalonia-specific, not session/runtime.");

        return failed > 0 ? 1 : 0;
    }

    // ================================================================
    // INFERENCE HELPERS
    // ================================================================

    static float[,] RunInference(InferenceSession session, float[] audio)
    {
        var tensor = new DenseTensor<float>(new[] { 1, audio.Length });
        for (int i = 0; i < audio.Length; i++)
            tensor[0, i] = audio[i];

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("audio", tensor)
        };

        using var results = session.Run(inputs);
        var output = results.First().AsTensor<float>();

        int frames = output.Dimensions[1];
        int classes = output.Dimensions[2];
        float[,] logits = new float[frames, classes];
        for (int t = 0; t < frames; t++)
            for (int c = 0; c < classes; c++)
                logits[t, c] = output[0, t, c];

        // Dump raw logits for external comparison
        string dumpPath = Path.Combine(AppContext.BaseDirectory, $"debug_ov2_logits.bin");
        using (var bw = new BinaryWriter(File.Open(dumpPath, FileMode.Create)))
        {
            bw.Write(frames);
            bw.Write(classes);
            for (int t = 0; t < frames; t++)
                for (int c = 0; c < classes; c++)
                    bw.Write(logits[t, c]);
        }
        Console.WriteLine($"│  Dumped logits to: {dumpPath}");

        return logits;
    }

    static float[,] RunInferenceWithGCPressure(InferenceSession session, float[] audio)
    {
        // Create tensor
        var tensor = new DenseTensor<float>(new[] { 1, audio.Length });
        for (int i = 0; i < audio.Length; i++)
            tensor[0, i] = audio[i];

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("audio", tensor)
        };

        // GC pressure AFTER tensor creation, BEFORE Run()
        for (int i = 0; i < 200; i++)
            _ = new byte[1024 * 1024];
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();

        using var results = session.Run(inputs);
        var output = results.First().AsTensor<float>();

        int frames = output.Dimensions[1];
        int classes = output.Dimensions[2];
        float[,] logits = new float[frames, classes];
        for (int t = 0; t < frames; t++)
            for (int c = 0; c < classes; c++)
                logits[t, c] = output[0, t, c];

        return logits;
    }

    // ================================================================
    // COMPARISON
    // ================================================================

    static bool CompareToReference(float[,] logits)
    {
        int frames = logits.GetLength(0);
        int classes = logits.GetLength(1);
        int refFrames = ReferenceLogits.GetLength(0);
        int refClasses = ReferenceLogits.GetLength(1);

        if (classes != refClasses)
        {
            Console.WriteLine($"│  ⚠️  Class count mismatch: {classes} vs ref {refClasses}");
            return false;
        }

        if (frames != refFrames)
        {
            Console.WriteLine($"│  ⚠️  Frame count mismatch: {frames} vs ref {refFrames}");
        }

        int compareFrames = Math.Min(frames, refFrames);
        double maxDiff = 0;
        double sumDiff = 0;
        int totalElements = compareFrames * classes;
        int worstT = 0, worstC = 0;

        for (int t = 0; t < compareFrames; t++)
        {
            for (int c = 0; c < classes; c++)
            {
                double diff = Math.Abs(logits[t, c] - ReferenceLogits[t, c]);
                sumDiff += diff;
                if (diff > maxDiff)
                {
                    maxDiff = diff;
                    worstT = t;
                    worstC = c;
                }
            }
        }

        double meanDiff = sumDiff / totalElements;

        Console.WriteLine($"│  Max diff:  {maxDiff:F8}  (frame {worstT}, class {worstC})");
        Console.WriteLine($"│  Mean diff: {meanDiff:F8}");

        if (maxDiff < 1e-3)
        {
            Console.WriteLine($"│  Verdict: PERFECT MATCH");
            return true;
        }
        else if (maxDiff < 0.01)
        {
            Console.WriteLine($"│  Verdict: ACCEPTABLE (numeric jitter)");
            return true;
        }
        else
        {
            Console.WriteLine($"│  Verdict: MISMATCH");
            // Show worst 3
            Console.WriteLine($"│  Worst: logit[{worstT},{worstC}] = {logits[worstT, worstC]:F4} vs ref {ReferenceLogits[worstT, worstC]:F4}");
            return false;
        }
    }

    // ================================================================
    // BINARY FILE LOADERS
    // ================================================================

    /// <summary>
    /// Loads [int32 rows, int32 cols, float32 data...] → returns 1D (flattened row if rows==1)
    /// </summary>
    static float[] LoadBinMatrix1D(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(fs);
        int rows = reader.ReadInt32();
        int cols = reader.ReadInt32();
        float[] data = new float[rows * cols];
        for (int i = 0; i < data.Length; i++)
            data[i] = reader.ReadSingle();
        return data;
    }

    /// <summary>
    /// Loads [int32 rows, int32 cols, float32 data...] → returns 2D array
    /// </summary>
    static float[,] LoadBinMatrix2D(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(fs);
        int rows = reader.ReadInt32();
        int cols = reader.ReadInt32();
        float[,] data = new float[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                data[r, c] = reader.ReadSingle();
        return data;
    }

    // ================================================================
    // SESSION HOLDER (mimics OnnxModelBase / RexEngine pattern)
    // ================================================================

    class SessionHolder
    {
        private readonly InferenceSession _session;

        public SessionHolder(string modelPath)
        {
            var opts = new SessionOptions();
            opts.AppendExecutionProvider_CPU();
            _session = new InferenceSession(modelPath, opts);
        }

        public float[,] Run(float[] audio)
        {
            return RunInference(_session, audio);
        }
    }
}
