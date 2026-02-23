using Microsoft.ML.OnnxRuntime;
using System;
using System.IO;

namespace Frontend.Core.Inference;

public abstract class OnnxModelBase : IDisposable
{
    protected InferenceSession? _session;

    protected void LoadSession(string modelPath, int gpuDeviceId = 0)
    {
        if (_session != null) return;

        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Model file not found: {modelPath}");

        var options = new SessionOptions();
        options.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR;

        try
        {
            options.AppendExecutionProvider_CUDA(gpuDeviceId);
            Console.WriteLine($"[OnnxModelBase] Loaded {Path.GetFileName(modelPath)} on CUDA:{gpuDeviceId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OnnxModelBase] CUDA failed: {ex.Message}. Falling back to CPU.");
        }
        options.AppendExecutionProvider_CPU();

        _session = new InferenceSession(modelPath, options);

        // Diagnostic: Report which native library is loaded
        Console.WriteLine($"[OnnxModelBase] Session created successfully");
        Console.WriteLine($"[OnnxModelBase] Model: {modelPath}");
        Console.WriteLine($"[OnnxModelBase] ORT Version: {OrtEnv.Instance().GetVersionString()}");

        // Find loaded onnxruntime native DLL
        foreach (System.Diagnostics.ProcessModule mod in System.Diagnostics.Process.GetCurrentProcess().Modules)
        {
            if (mod.ModuleName != null && mod.ModuleName.Contains("onnxruntime", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[OnnxModelBase] Native DLL: {mod.FileName} ({mod.ModuleMemorySize} bytes)");
            }
        }
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }
}
