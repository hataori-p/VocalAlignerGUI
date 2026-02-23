using System;

namespace Frontend.Services.Logging;

public interface ILogSink : IDisposable
{
    void Write(LogLevel level, string message);
}

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Fatal
}
