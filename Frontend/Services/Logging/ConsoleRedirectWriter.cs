using System;
using System.IO;
using System.Text;

namespace Frontend.Services.Logging;

/// <summary>
/// Replaces Console.Out so that all existing Console.WriteLine calls
/// in the codebase are automatically forwarded to AppLogger.
/// Zero changes required to any existing code.
/// </summary>
public class ConsoleRedirectWriter : TextWriter
{
    [ThreadStatic]
    private static bool _isRedirecting;

    private readonly StringBuilder _buffer = new();

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        if (_isRedirecting) return;
        _isRedirecting = true;
        try
        {
            if (value == '\n')
            {
                // Strip trailing \r if present (Windows line endings)
                string line = _buffer.ToString().TrimEnd('\r');
                _buffer.Clear();
                if (!string.IsNullOrWhiteSpace(line))
                    AppLogger.Info(line);
            }
            else
            {
                _buffer.Append(value);
            }
        }
        finally
        {
            _isRedirecting = false;
        }
    }

    public override void Write(string? value)
    {
        if (_isRedirecting) return;
        if (value == null) return;
        _isRedirecting = true;
        try
        {
            foreach (char c in value)
            {
                if (c == '\n')
                {
                    string line = _buffer.ToString().TrimEnd('\r');
                    _buffer.Clear();
                    if (!string.IsNullOrWhiteSpace(line))
                        AppLogger.Info(line);
                }
                else
                {
                    _buffer.Append(c);
                }
            }
        }
        finally
        {
            _isRedirecting = false;
        }
    }

    public override void WriteLine(string? value)
    {
        if (_isRedirecting) return;
        _isRedirecting = true;
        try
        {
            AppLogger.Info(value ?? string.Empty);
        }
        finally
        {
            _isRedirecting = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        // Flush any remaining buffer content
        if (_buffer.Length > 0)
        {
            AppLogger.Info(_buffer.ToString());
            _buffer.Clear();
        }
        base.Dispose(disposing);
    }
}
