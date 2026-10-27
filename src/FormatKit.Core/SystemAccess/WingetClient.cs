namespace FormatKit.Core.SystemAccess;

public enum WingetStatus { Installed, AlreadyInstalled, Failed, WingetMissing }

public sealed record WingetResult(WingetStatus Status, int ExitCode, string Output);

public class WingetClient(IProcessRunner runner)
{
    private const int AlreadyInstalledA = -1978335189; // 0x8A15002B
    private const int AlreadyInstalledB = -1978335135; // 0x8A150061

    public async Task<WingetResult> InstallAsync(string id, CancellationToken ct)
    {
        ProcessResult result;
        try
        {
            result = await runner.RunAsync(
                new ProcessSpec(
                    "winget.exe",
                    ["install", "--id", id, "--exact", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"],
                    Timeouts.Install),
                ct).ConfigureAwait(false);
        }
        catch (FileNotFoundException ex)
        {
            return new WingetResult(WingetStatus.WingetMissing, -1, ex.Message);
        }

        var output = result.StdOut + result.StdErr;
        if (result.TimedOut)
        {
            return new WingetResult(WingetStatus.Failed, result.ExitCode, output);
        }

        var status = result.ExitCode switch
        {
            0 => WingetStatus.Installed,
            AlreadyInstalledA or AlreadyInstalledB => WingetStatus.AlreadyInstalled,
            _ => WingetStatus.Failed,
        };
        return new WingetResult(status, result.ExitCode, output);
    }

    public async Task<bool> IsInstalledAsync(string id, CancellationToken ct)
    {
        try
        {
            var result = await runner.RunAsync(
                new ProcessSpec(
                    "winget.exe",
                    ["list", "--id", id, "--exact", "--accept-source-agreements", "--disable-interactivity"],
                    Timeouts.Query),
                ct).ConfigureAwait(false);
            return !result.TimedOut && result.ExitCode == 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }
}
