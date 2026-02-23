using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OnnxVerify;

class Program
{
    static int Main(string[] args)
    {
        // Hardcoded paths relative to repo root (assuming execution from Frontend/Tests/OnnxVerify)
        const string InputPath  = @"../../../Backend/debug_python_input.bin";
        const string ModelPath  = @"../../resources/models/rex_model.onnx";
        const string OutputPath = @"../../../Backend/debug_csharp_logits.bin";

        try
        {
            Console.WriteLine("[C# ONNX] Loading audio binary...");
            float[] audio = LoadAudioAsFloat32Mono(InputPath);

            Console.WriteLine($"[C# ONNX] Audio loaded: {audio.Length} samples");

            Console.WriteLine("[C# ONNX] Loading ONNX model with CUDA fallback...");
            using var options = new SessionOptions();
            options.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_WARNING;

            try
            {
                options.AppendExecutionProvider_CUDA(0);
                Console.WriteLine("[C# ONNX] ✅ CUDA execution provider loaded.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[C# ONNX] ⚠️  CUDA failed: {ex.Message}. Falling back to CPU.");
                options.AppendExecutionProvider_CPU();
            }

            using var session = new InferenceSession(ModelPath, options);

            // Create input tensor: [1, N]
            var inputTensor = new DenseTensor<float>(new[] { 1, audio.Length });
            for (int i = 0; i < audio.Length; i++)
            {
                inputTensor[0, i] = audio[i];
            }

            // Dump the *input tensor* (not logits!)
            string debugInputPath = @"../../../Backend/debug_csharp_onnx_input.bin";
            using var fsInput = new FileStream(debugInputPath, FileMode.Create);
            using var bw = new BinaryWriter(fsInput);
            bw.Write(1); bw.Write(audio.Length);
            foreach (var f in audio) bw.Write(f); // float32
            Console.WriteLine($"✅ Dumped C# ONNX input: {audio.Length} samples");

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("audio", inputTensor)
            };

            Console.WriteLine("[C# ONNX] Running inference...");
            using var output = session.Run(inputs);
            var logitsTensor = output.First().AsTensor<float>(); // [1, Frames, Classes]

            // Flatten for binary dump
            var logits = logitsTensor.ToArray();

            // Save as binary: first 4 bytes = total float count, then raw float32s
            var totalFloats = logits.Length;
            using var fsLogits = new FileStream(OutputPath, FileMode.Create);
            using var writer = new BinaryWriter(fsLogits);
            writer.Write(totalFloats);
            foreach (var f in logits)
            {
                writer.Write(f);
            }

            Console.WriteLine($"[C# ONNX] Success! Logits saved to {OutputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[C# ONNX] FAILED: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    // Load .bin (raw float32, little-endian, 16kHz, mono)
    static float[] LoadAudioAsFloat32Mono(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Input file not found: {path}");

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(fs);

        // Expected format (from debug_python_input.bin):
        //  - 4 bytes: channel count (int32)
        //  - 4 bytes: sample count (int32)
        //  - N * 4 bytes: float32 (mono)
        
        if (fs.Length < 8)
            throw new InvalidDataException("Input file too small to contain header");

        int channels = reader.ReadInt32();
        int samples  = reader.ReadInt32();

        if (channels != 1)
            throw new InvalidDataException($"Expected mono audio, but header reports {channels} channels");

        long expectedDataBytes = (long)samples * sizeof(float);
        if (fs.Length - 8 != expectedDataBytes)
            throw new InvalidDataException(
                $"Header says {samples} samples, but file has {fs.Length - 8} bytes of data (expected {expectedDataBytes})");

        var floats = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            floats[i] = reader.ReadSingle();
        }
        return floats;
    }
}
