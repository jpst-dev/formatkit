namespace FormatKit.Core.SystemAccess;

public sealed class PowerShellPrinters(IProcessRunner runner) : IPrinters
{
    internal const string Script = "Get-Printer -ErrorAction Stop | Select-Object -ExpandProperty Name";

    public async Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct)
    {
        var result = await runner.RunAsync(
            new ProcessSpec("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", Script], Timeouts.Query),
            ct).ConfigureAwait(false);

        if (result.ExitCode != 0 || result.TimedOut)
            throw new InvalidOperationException($"Listing printers failed (exit {result.ExitCode}): {result.StdErr.Trim()}");

        return result.StdOut
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}
