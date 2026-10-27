using FormatKit.Core.Localization;
using Serilog;

namespace FormatKit.Core.SystemAccess;

public sealed class ConsoleRebooter : IRebooter
{
    private readonly IProcessRunner _runner;
    private readonly ILogger _log;
    private readonly Func<bool> _escPressed;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action<string> _write;

    public ConsoleRebooter(IProcessRunner runner, ILogger? log = null)
        : this(runner, log ?? Serilog.Core.Logger.None, ConsoleEscPressed, Task.Delay, Console.WriteLine)
    {
    }

    internal ConsoleRebooter(
        IProcessRunner runner,
        ILogger log,
        Func<bool> escPressed,
        Func<TimeSpan, CancellationToken, Task> delay,
        Action<string> write)
    {
        _runner = runner;
        _log = log;
        _escPressed = escPressed;
        _delay = delay;
        _write = write;
    }

    public async Task<bool> RebootAsync(TimeSpan countdown, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + countdown;
        var nextPrint = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (_escPressed())
            {
                _write(Strings.Get("Reboot_Cancelled"));
                return false;
            }

            if (DateTime.UtcNow >= nextPrint)
            {
                var remaining = (int)Math.Ceiling((deadline - DateTime.UtcNow).TotalSeconds);
                _write(Strings.Format("Reboot_Countdown", remaining));
                nextPrint = DateTime.UtcNow.AddSeconds(10);
            }

            await _delay(TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(false);
        }

        try
        {
            var result = await _runner.RunAsync(
                new ProcessSpec("shutdown.exe", ["/r", "/t", "0"], Timeouts.Query), ct).ConfigureAwait(false);
            if (result.ExitCode == 0 && !result.TimedOut)
            {
                return true;
            }

            _log.Warning(
                "shutdown.exe failed (exit {ExitCode}, timedOut {TimedOut}): {StdErr}",
                result.ExitCode, result.TimedOut, result.StdErr);
        }
        catch (FileNotFoundException ex)
        {
            _log.Warning(ex, "shutdown.exe not found");
        }

        _write(Strings.Get("Reboot_ShutdownFailed"));
        return false;
    }

    private static bool ConsoleEscPressed() =>
        !Console.IsInputRedirected && Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Escape;
}
