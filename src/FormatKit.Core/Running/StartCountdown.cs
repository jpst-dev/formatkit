using System.Diagnostics;
using FormatKit.Core.Localization;

namespace FormatKit.Core.Running;

/// <summary>
/// The pause before an automatic run (first logon): prints the time left every 10 s and
/// lets the user cancel with Esc, like the reboot countdown.
/// </summary>
public sealed class StartCountdown
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan PrintInterval = TimeSpan.FromSeconds(10);

    private readonly Func<bool> _escPressed;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action<string> _write;

    public StartCountdown()
        : this(ConsoleEscPressed, Task.Delay, Console.WriteLine)
    {
    }

    internal StartCountdown(Func<bool> escPressed, Func<TimeSpan, CancellationToken, Task> delay, Action<string> write)
    {
        _escPressed = escPressed;
        _delay = delay;
        _write = write;
    }

    /// <summary>True to proceed with the run, false when Esc was pressed.</summary>
    public async Task<bool> WaitAsync(string profileName, TimeSpan countdown, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var polled = TimeSpan.Zero;
        var nextPrint = TimeSpan.Zero;
        while (true)
        {
            // The larger of the wall clock and the requested delays: exact in real use, finite with a fake delay.
            var elapsed = clock.Elapsed > polled ? clock.Elapsed : polled;
            if (elapsed >= countdown)
                return true;

            ct.ThrowIfCancellationRequested();

            if (_escPressed())
            {
                _write(Strings.Get("Start_Cancelled"));
                return false;
            }

            if (elapsed >= nextPrint)
            {
                var remaining = (int)Math.Ceiling((countdown - elapsed).TotalSeconds);
                _write(Strings.Format("Start_Countdown", profileName, remaining));
                nextPrint = elapsed + PrintInterval;
            }

            await _delay(PollInterval, ct).ConfigureAwait(false);
            polled += PollInterval;
        }
    }

    private static bool ConsoleEscPressed() =>
        !Console.IsInputRedirected && Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Escape;
}
