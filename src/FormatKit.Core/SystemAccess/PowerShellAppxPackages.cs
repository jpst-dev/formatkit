using System.Text.RegularExpressions;

namespace FormatKit.Core.SystemAccess;

public sealed partial class PowerShellAppxPackages(IProcessRunner runner) : IAppxPackages
{
    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex SafeName();

    // The script prints only the literals True/False, never localized text.
    [GeneratedRegex(@"^(True|False);(True|False)$")]
    private static partial Regex StatePattern();

    public async Task<AppxState> GetStateAsync(string packageName, CancellationToken ct)
    {
        Validate(packageName);
        var output = await RunAsync(
            $"$u=[bool](Get-AppxPackage -Name '{packageName}'); "
            + $"$p=[bool](Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq '{packageName}'); \"$u;$p\"",
            Timeouts.Query,
            "Get-AppxPackage",
            ct).ConfigureAwait(false);

        var match = StatePattern().Match(output.Trim());
        return match.Success
            ? new AppxState(match.Groups[1].Value == "True", match.Groups[2].Value == "True")
            : throw new InvalidOperationException($"Get-AppxPackage printed unexpected output for '{packageName}'.");
    }

    public async Task RemoveAsync(string packageName, CancellationToken ct)
    {
        Validate(packageName);
        await RunAsync(
            $"Get-AppxPackage -Name '{packageName}' | Remove-AppxPackage; "
            + $"Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq '{packageName}' | Remove-AppxProvisionedPackage -Online | Out-Null",
            Timeouts.Install,
            "Remove-AppxPackage",
            ct).ConfigureAwait(false);
    }

    private static void Validate(string packageName)
    {
        if (!SafeName().IsMatch(packageName))
        {
            throw new ArgumentException($"Invalid AppX package name '{packageName}'.", nameof(packageName));
        }
    }

    private async Task<string> RunAsync(string script, TimeSpan timeout, string operation, CancellationToken ct)
    {
        var result = await runner.RunAsync(
            new ProcessSpec("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], timeout),
            ct).ConfigureAwait(false);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException(
                $"{operation} failed (exit {result.ExitCode}): {result.StdErr.Trim()}");
        }

        return result.StdOut;
    }
}
