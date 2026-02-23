using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Frontend.Services.Logging;

/// <summary>
/// Central logger. Routes all log calls to registered sinks.
/// Console.WriteLine and Debug.WriteLine are automatically redirected
/// here via ConsoleRedirectWriter and DebugTraceListener.
/// </summary>
public static class AppLogger
{
    private static readonly List<ILogSink> _sinks = new();
    private static readonly object _lock = new();

    // --- Sink Management ---

    public static void AddSink(ILogSink sink)
    {
        lock (_lock) _sinks.Add(sink);
    }

    public static void RemoveSink<T>() where T : ILogSink
    {
        lock (_lock)
        {
            var target = _sinks.OfType<T>().FirstOrDefault();
            if (target is null) return;
            _sinks.Remove(target);
            target.Dispose();
        }
    }

    public static bool HasSink<T>() where T : ILogSink =>
        _sinks.OfType<T>().Any();

    public static string? ActiveLogFilePath
    {
        get
        {
            lock (_lock)
                return _sinks.OfType<FileSink>().FirstOrDefault()?.FilePath;
        }
    }

    // --- Logging API ---

    public static void Debug(string message)   => Write(LogLevel.Debug,   message);
    public static void Info(string message)    => Write(LogLevel.Info,    message);
    public static void Warning(string message) => Write(LogLevel.Warning, message);
    public static void Error(string message)   => Write(LogLevel.Error,   message);

    /// <summary>
    /// Always writes to a crash file on disk regardless of active sinks.
    /// Also routes through normal sinks if any are active.
    /// </summary>
    public static void Fatal(Exception ex, string context = "")
    {
        string msg = $"[{context}] {ex.Message}\n{ex.StackTrace}";
        Write(LogLevel.Fatal, msg);

        // Unconditional crash dump — bypasses all sink state
        try
        {
            string crashPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "VocalAligner_CRASH.txt");
            File.AppendAllText(crashPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FATAL [{context}]\n{ex}\n\n");
        }
        catch { /* truly last resort */ }
    }

    public static void Shutdown()
    {
        lock (_lock)
        {
            foreach (var sink in _sinks) sink.Dispose();
            _sinks.Clear();
        }
    }

    // --- Internal ---

    private static void Write(LogLevel level, string message)
    {
        lock (_lock)
        {
            foreach (var sink in _sinks)
            {
                try { sink.Write(level, message); }
                catch { /* a broken sink must never crash the app */ }
            }
        }
    }
}
