using System;
using System.IO;

namespace Frontend.Services.Logging;

/// <summary>
/// Appends log entries to a timestamped file.
/// Thread-safe — safe to call from ONNX inference threads.
/// </summary>
public class FileSink : ILogSink
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();

    public string FilePath { get; }

    public FileSink(string filePath)
    {
        FilePath = filePath;
        _writer = new StreamWriter(filePath, append: true, System.Text.Encoding.UTF8)
        {
            AutoFlush = true
        };
        _writer.WriteLine($"=== VocalAlignerGUI Log Started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
    }

    public void Write(LogLevel level, string message)
    {
        lock (_lock)
        {
            _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}");
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer.WriteLine($"=== Log Closed {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            _writer.Dispose();
        }
    }
}
