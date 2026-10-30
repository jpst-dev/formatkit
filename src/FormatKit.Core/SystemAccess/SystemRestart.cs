using Serilog;

namespace FormatKit.Core.SystemAccess;

/// <summary>Restarts Windows immediately; shared by every host's rebooter.</summary>
public static class SystemRestart
{
    /// <summary>Runs <c>shutdown.exe /r /t 0</c>. Returns false (and logs) when it fails or is missing.</summary>
    public static async Task<bool> RestartAsync(IProcessRunner runner, ILogger log, CancellationToken ct)
    {
        try
        {
            var result = await runner.RunAsync(
                new ProcessSpec("shutdown.exe", ["/r", "/t", "0"], Timeouts.Query), ct).ConfigureAwait(false);
            if (result.ExitCode == 0 && !result.TimedOut)
            {
                return true;
            }

            log.Warning(
                "shutdown.exe failed (exit {ExitCode}, timedOut {TimedOut}): {StdErr}",
                result.ExitCode, result.TimedOut, result.StdErr);
        }
        catch (FileNotFoundException ex)
        {
            log.Warning(ex, "shutdown.exe not found");
        }

        return false;
    }
}
