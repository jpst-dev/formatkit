using FormatKit.Core.SystemAccess;

namespace FormatKit.Dev;

/// <summary>The effective Windows PowerShell 5.1 execution policy, read from the registry (spec 12: Restricted on a clean Windows).</summary>
internal static class PowerShellExecutionPolicy
{
    private const string PolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\PowerShell";
    private const string ShellKey = @"SOFTWARE\Microsoft\PowerShell\1\ShellIds\Microsoft.PowerShell";

    /// <summary>
    /// First non-empty wins: machine policy, user policy (each only when EnableScripts is 1; 0 means Restricted),
    /// then the current user's setting, then the machine's; none is Restricted.
    /// </summary>
    public static string Effective(IRegistry registry)
    {
        foreach (var hive in new[] { "HKLM", "HKCU" })
        {
            var enabled = registry.Read($@"{hive}\{PolicyKey}", "EnableScripts");
            if (enabled is null)
                continue;
            if (enabled.Data != "1")
                return "Restricted";
            var policy = Read(registry, $@"{hive}\{PolicyKey}");
            if (policy is not null)
                return policy;
        }

        return Read(registry, $@"HKCU\{ShellKey}") ?? Read(registry, $@"HKLM\{ShellKey}") ?? "Restricted";
    }

    public static bool RunsScripts(string policy) =>
        policy.Equals("RemoteSigned", StringComparison.OrdinalIgnoreCase)
        || policy.Equals("Unrestricted", StringComparison.OrdinalIgnoreCase)
        || policy.Equals("Bypass", StringComparison.OrdinalIgnoreCase);

    private static string? Read(IRegistry registry, string key)
    {
        var value = registry.Read(key, "ExecutionPolicy");
        return value is { Kind: RegistryKind.String } && !string.IsNullOrWhiteSpace(value.Data) ? value.Data.Trim() : null;
    }
}
