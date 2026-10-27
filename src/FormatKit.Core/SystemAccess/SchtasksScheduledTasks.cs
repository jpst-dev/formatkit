namespace FormatKit.Core.SystemAccess;

public sealed class SchtasksScheduledTasks(IProcessRunner runner, string exePath) : IScheduledTasks
{
    private const string TaskName = "FormatKit\\Resume";

    public async Task RegisterResumeTaskAsync(CancellationToken ct)
    {
        var result = await runner.RunAsync(
            new ProcessSpec(
                "schtasks.exe",
                ["/Create", "/TN", TaskName, "/TR", $"\"{exePath}\" run --resume", "/SC", "ONLOGON", "/RL", "HIGHEST", "/F"],
                Timeouts.Query),
            ct).ConfigureAwait(false);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException(
                $"Failed to register the resume scheduled task (exit {result.ExitCode}): {result.StdErr.Trim()}");
        }
    }

    public async Task RemoveResumeTaskAsync(CancellationToken ct)
    {
        var result = await runner.RunAsync(
            new ProcessSpec("schtasks.exe", ["/Delete", "/TN", TaskName, "/F"], Timeouts.Query),
            ct).ConfigureAwait(false);

        if (result.ExitCode == 0)
        {
            return;
        }

        // A missing task reports "ERROR: ..."; that is fine on removal.
        if (!result.TimedOut
            && (result.StdErr.Contains("ERROR:", StringComparison.Ordinal)
                || result.StdOut.Contains("ERROR:", StringComparison.Ordinal)))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Failed to remove the resume scheduled task (exit {result.ExitCode}): {result.StdErr.Trim()}");
    }
}
