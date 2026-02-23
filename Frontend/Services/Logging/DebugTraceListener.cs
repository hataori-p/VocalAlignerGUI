using System;
using System.Diagnostics;

namespace Frontend.Services.Logging;

/// <summary>
/// Hooks into System.Diagnostics.Trace/Debug so that all Debug.WriteLine
/// calls are forwarded to AppLogger.
/// Zero changes required to any existing code.
/// </summary>
public class DebugTraceListener : TraceListener
{
    private static readonly string[] _suppressPatterns = new[]
    {
        "PlatformImpl is null",
        "couldn't handle input",
    };

    private static bool ShouldSuppress(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return true;
        foreach (var pattern in _suppressPatterns)
            if (message.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    public override void Write(string? message)
    {
        if (!ShouldSuppress(message))
            AppLogger.Debug(message!);
    }

    public override void WriteLine(string? message)
    {
        if (!ShouldSuppress(message))
            AppLogger.Debug(message!);
    }
}
