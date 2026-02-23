using System;

namespace Frontend.Services.Logging;

/// <summary>
/// Writes log entries to the system console with color coding.
/// Only added in DEBUG builds — never compiled into Release.
/// </summary>
public class ConsoleSink : ILogSink
{
    private readonly System.IO.TextWriter _out;

    public ConsoleSink(System.IO.TextWriter originalOut)
    {
        _out = originalOut;
    }

    public void Write(LogLevel level, string message)
    {
        var color = level switch
        {
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error   => ConsoleColor.Red,
            LogLevel.Fatal   => ConsoleColor.DarkRed,
            LogLevel.Debug   => ConsoleColor.DarkGray,
            _                => ConsoleColor.Gray
        };

        Console.ForegroundColor = color;
        _out.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}");
        Console.ResetColor();
    }

    public void Dispose() => _out.Flush();
}
