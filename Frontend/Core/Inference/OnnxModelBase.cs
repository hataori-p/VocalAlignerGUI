using Microsoft.ML.OnnxRuntime;
using System;
using System.IO;

namespace Frontend.Core.Inference;

public abstract class OnnxModelBase : IDisposable
{
    protected InferenceSession? _session;

    public bool IsGpuAccelerated { get; private set; } = false;

    protected void LoadSession(string modelPath, int gpuDeviceId = 0)
    {
        if (_session != null) return;

        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Model file not found: {modelPath}");

        var options = new SessionOptions();
        options.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR;

        bool cudaRequested = false;
        try
        {
            options.AppendExecutionProvider_CUDA(gpuDeviceId);
            cudaRequested = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OnnxModelBase] CUDA EP registration failed: {ex.Message}. Falling back to CPU.");
        }
        options.AppendExecutionProvider_CPU();

        _session = new InferenceSession(modelPath, options);

        if (cudaRequested)
        {
            // Detect CUDA by checking if the CUDA provider DLL was loaded into the process
            // after session creation — this is reliable across ORT 1.x versions.
            foreach (System.Diagnostics.ProcessModule mod in
                     System.Diagnostics.Process.GetCurrentProcess().Modules)
            {
                if (mod.ModuleName != null &&
                    mod.ModuleName.Contains("onnxruntime_providers_cuda",
                        StringComparison.OrdinalIgnoreCase))
                {
                    IsGpuAccelerated = true;
                    break;
                }
            }

            Console.WriteLine(IsGpuAccelerated
                ? $"[OnnxModelBase] Confirmed CUDA execution for {Path.GetFileName(modelPath)}"
                : $"[OnnxModelBase] CUDA requested but ORT is using CPU for {Path.GetFileName(modelPath)}");
        }

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
        IsGpuAccelerated = false;
    }
}
