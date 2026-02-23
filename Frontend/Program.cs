using Avalonia;
using System;

namespace Frontend;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
#if DEBUG
        // Capture original stdout BEFORE replacing Console.Out
        var originalOut = Console.Out;

        // In DEBUG builds: add the console sink and hook Debug.WriteLine
        Frontend.Services.Logging.AppLogger.AddSink(new Frontend.Services.Logging.ConsoleSink(originalOut));

        // Redirect Debug.WriteLine → AppLogger
        System.Diagnostics.Trace.Listeners.Clear();
        System.Diagnostics.Trace.Listeners.Add(new Frontend.Services.Logging.DebugTraceListener());

        Frontend.Services.Logging.AppLogger.Debug("DEBUG build — console sink active.");
#endif

        // Redirect Console.WriteLine → AppLogger (both DEBUG and Release)
        // In Release + WinExe there is no console window, but the redirect
        // ensures all existing Console.WriteLine calls flow into the logger
        // and reach any active FileSink.
        Console.SetOut(new Frontend.Services.Logging.ConsoleRedirectWriter());

        // Global unhandled exception → crash dump
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Frontend.Services.Logging.AppLogger.Fatal(ex, "UnhandledException");
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Frontend.Services.Logging.AppLogger.Fatal(ex, "App Startup");
            throw;
        }
        finally
        {
            Frontend.Services.Logging.AppLogger.Shutdown();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
