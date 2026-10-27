using System.Text.RegularExpressions;

namespace FormatKit.Core.SystemAccess;

public sealed partial class PowerShellOptionalFeatures(IProcessRunner runner) : IOptionalFeatures
{
    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex SafeName();

    public async Task<OptionalFeatureState> GetStateAsync(string name, CancellationToken ct)
    {
        Validate(name);
        var output = await RunAsync(
            $"$f=Get-WindowsOptionalFeature -Online -FeatureName '{name}' -ErrorAction SilentlyContinue; if(!$f){{'Absent'}}else{{[string]$f.State}}",
            Timeouts.Query,
            "Get-WindowsOptionalFeature",
            ct).ConfigureAwait(false);

        return output.Trim() switch
        {
            "Enabled" => OptionalFeatureState.Enabled,
            "Disabled" or "DisabledWithPayloadRemoved" => OptionalFeatureState.Disabled,
            "Absent" => OptionalFeatureState.Absent,
            var other => throw new InvalidOperationException($"Unexpected feature state '{other}' for '{name}'."),
        };
    }

    public async Task<bool> SetStateAsync(string name, bool enabled, CancellationToken ct)
    {
        Validate(name);
        var cmdlet = enabled ? "Enable-WindowsOptionalFeature" : "Disable-WindowsOptionalFeature";
        var output = await RunAsync(
            $"[string]({cmdlet} -Online -FeatureName '{name}' -NoRestart).RestartNeeded",
            Timeouts.Install,
            cmdlet,
            ct).ConfigureAwait(false);

        return output.Trim() switch
        {
            "True" => true,
            "False" => false,
            var other => throw new InvalidOperationException($"{cmdlet} printed unexpected output '{other}'."),
        };
    }

    private static void Validate(string name)
    {
        if (!SafeName().IsMatch(name))
        {
            throw new ArgumentException($"Invalid optional feature name '{name}'.", nameof(name));
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
