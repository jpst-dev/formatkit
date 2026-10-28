using Serilog;
using Serilog.Events;

namespace FormatKit.Core.Logging;

public static class LogSetup
{
    /// <summary>
    /// Creates the run logger. The returned logger implements <see cref="IDisposable"/>;
    /// dispose it to flush and release the log file.
    /// </summary>
    public static Serilog.ILogger Create(AppPaths paths, TimeProvider clock, bool verbose)
    {
        var stamp = clock.GetLocalNow().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var file = Path.Combine(paths.LogsDirectory, $"run-{stamp}-{Environment.ProcessId}.log");
        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(file)
            .WriteTo.Console(
                restrictedToMinimumLevel: verbose ? LogEventLevel.Debug : LogEventLevel.Warning,
                // Stack traces stay in the log file unless --verbose asks for them on screen.
                outputTemplate: verbose ? "{Message:lj}{NewLine}{Exception}" : "{Message:lj}{NewLine}")
            .CreateLogger();
    }
}
