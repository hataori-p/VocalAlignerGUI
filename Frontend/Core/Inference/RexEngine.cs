using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Frontend.Core.Inference;

public class RexEngine : OnnxModelBase
{
    private readonly string _inputName = "audio"; // Must match export_rex.py
    
    // Maps Phoneme String -> Output Index
    public Dictionary<string, int> Vocab { get; private set; } = new();
    public Dictionary<int, string> ReverseVocab { get; private set; } = new();

    public RexEngine(string modelPath)
    {
        LoadSession(modelPath);
        
        // Assuming vocab.txt exists next to model
        string vocabPath = System.IO.Path.ChangeExtension(modelPath, ".txt");
        Console.WriteLine($"[RexEngine] Loading Vocab from: {vocabPath}");

        if (System.IO.File.Exists(vocabPath))
        {
            var lines = System.IO.File.ReadAllLines(vocabPath);
            for(int i = 0; i < lines.Length; i++)
            {
                string token = lines[i].Trim();
                Vocab[token] = i;
                ReverseVocab[i] = token;
            }
            Console.WriteLine($"[RexEngine] Vocab loaded. Count: {Vocab.Count}. Sample: {string.Join(", ", lines.Take(5))}");
        }
        else
        {
            Console.WriteLine($"[RexEngine] CRITICAL: Vocab file not found!");
        }
    }

    /// <summary>
    /// Runs inference on 16kHz audio.
    /// Returns [Frames, Classes] logits array.
    /// </summary>
    public float[,] Forward(float[] audio16k)
    {
        if (_session == null) throw new InvalidOperationException("Session not loaded");
        
        Console.WriteLine($"[RexEngine] Running inference on {audio16k.Length} samples ({audio16k.Length / 16000.0:F2}s)");

        // Prepare Tensor: [1, Length]
        var inputTensor = new DenseTensor<float>(new[] { 1, audio16k.Length });
        for (int i = 0; i < audio16k.Length; i++)
        {
            inputTensor[0, i] = audio16k[i];
        }
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, inputTensor) };

        using var results = _session.Run(inputs);
        
        // Output shape: [1, Frames, Classes]
        var outputTensor = results.First().AsTensor<float>();
        var dims = outputTensor.Dimensions; // [1, T, C]
        
        int frames = dims[1];
        int classes = dims[2];

        Console.WriteLine($"[RexEngine] Output: {frames} frames × {classes} classes");

        float[,] logits = new float[frames, classes];
        for (int t = 0; t < frames; t++)
        {
            for (int c = 0; c < classes; c++)
            {
                logits[t, c] = outputTensor[0, t, c];
            }
        }

        return logits;
    }
}
