namespace FormatKit.Core.SystemAccess;

public sealed record PnpUtilResult(bool Success, bool RebootRequired, int ExitCode, string Output);

public class PnpUtil(IProcessRunner runner)
{
    private const int RebootRequiredExit = 3010;

    public Task<PnpUtilResult> ExportDriverAsync(string infName, string outputDir, CancellationToken ct) =>
        RunAsync(["/export-driver", infName, outputDir], ct);

    public Task<PnpUtilResult> AddDriversAsync(string directory, CancellationToken ct) =>
        RunAsync(["/add-driver", Path.Combine(directory, "*.inf"), "/subdirs", "/install"], ct);

    private async Task<PnpUtilResult> RunAsync(string[] args, CancellationToken ct)
    {
        var result = await runner.RunAsync(new ProcessSpec("pnputil.exe", args, Timeouts.Install), ct).ConfigureAwait(false);
        var output = result.StdOut + result.StdErr;
        if (result.TimedOut)
        {
            return new PnpUtilResult(false, false, result.ExitCode, output);
        }

        return result.ExitCode switch
        {
            0 => new PnpUtilResult(true, false, 0, output),
            RebootRequiredExit => new PnpUtilResult(true, true, RebootRequiredExit, output),
            _ => new PnpUtilResult(false, false, result.ExitCode, output),
        };
    }
}
