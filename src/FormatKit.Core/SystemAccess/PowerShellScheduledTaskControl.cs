namespace FormatKit.Core.SystemAccess;

public sealed class PowerShellScheduledTaskControl(IProcessRunner runner) : IScheduledTaskControl
{
    public async Task<bool?> IsEnabledAsync(string path, string name, CancellationToken ct)
    {
        var output = await RunAsync(
            $"$t=@(Get-ScheduledTask -TaskPath {PowerShellText.SingleQuote(path)} -TaskName {PowerShellText.SingleQuote(name)} -ErrorAction SilentlyContinue); if($t.Count -eq 0){{'Absent'}}elseif($t.Count -gt 1){{'Multiple'}}else{{[string]$t[0].State}}",
            "Get-ScheduledTask",
            ct).ConfigureAwait(false);

        var state = output.Trim();
        return state switch
        {
            "Absent" => null,
            "Multiple" => throw new InvalidOperationException($"More than one scheduled task matches '{path}{name}'."),
            "Disabled" => false,
            "Ready" or "Running" or "Queued" => true,
            _ => throw new InvalidOperationException($"Unexpected scheduled task state '{state}' for '{path}{name}'."),
        };
    }

    public async Task SetEnabledAsync(string path, string name, bool enabled, CancellationToken ct)
    {
        var cmdlet = enabled ? "Enable-ScheduledTask" : "Disable-ScheduledTask";
        await RunAsync(
            $"$null={cmdlet} -TaskPath {PowerShellText.SingleQuote(path)} -TaskName {PowerShellText.SingleQuote(name)} -ErrorAction Stop",
            cmdlet,
            ct).ConfigureAwait(false);
    }

    private async Task<string> RunAsync(string script, string operation, CancellationToken ct)
    {
        var result = await runner.RunAsync(
            new ProcessSpec("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], Timeouts.Query),
            ct).ConfigureAwait(false);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException($"{operation} failed (exit {result.ExitCode}): {result.StdErr.Trim()}");
        }

        return result.StdOut;
    }
}
