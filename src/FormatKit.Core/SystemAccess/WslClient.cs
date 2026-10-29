namespace FormatKit.Core.SystemAccess;

/// <summary>Adapter over wsl.exe. Commands are passed through as-is; callers give absolute Linux paths for --exec.</summary>
public sealed class WslClient(IProcessRunner processes)
{
    public const string Exe = "wsl.exe";

    /// <summary>Constant script for <see cref="WriteFileAsync"/>: path and content arrive as $1 and $2, never interpolated.</summary>
    internal const string WriteScript = "printf '%s' \"$2\" > \"$1\"";

    private static readonly IReadOnlyDictionary<string, string> Env =
        new Dictionary<string, string> { ["WSL_UTF8"] = "1" };

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        try
        {
            var result = await processes.RunAsync(Spec(["--status"], Timeouts.Query), ct).ConfigureAwait(false);
            return !result.TimedOut && result.ExitCode == 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    public Task<ProcessResult> InstallPlatformAsync(CancellationToken ct) =>
        processes.RunAsync(Spec(["--install", "--no-distribution"], Timeouts.Install), ct);

    public async Task<IReadOnlyList<string>> ListDistrosAsync(CancellationToken ct)
    {
        var result = await processes.RunAsync(Spec(["--list", "--quiet"], Timeouts.Query), ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return [];
        }

        // The inbox stub can emit UTF-16 even with WSL_UTF8=1, which decodes with NULs between characters.
        return
        [
            .. result.StdOut
                .Replace("\0", "")
                .Replace("﻿", "")
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0),
        ];
    }

    public Task<ProcessResult> InstallDistroAsync(string distro, CancellationToken ct) =>
        processes.RunAsync(Spec(["--install", distro, "--no-launch"], Timeouts.Install), ct);

    public Task<ProcessResult> ExecAsync(
        string distro, string user, IReadOnlyList<string> command, CancellationToken ct, TimeSpan? timeout = null) =>
        processes.RunAsync(ExecSpec(distro, user, command, timeout ?? Timeouts.Query), ct);

    public Task<ProcessResult> WriteFileAsync(
        string distro, string user, string path, string content, CancellationToken ct) =>
        ExecAsync(distro, user, ["sh", "-c", WriteScript, "sh", path, content], ct);

    public Task TerminateAsync(string distro, CancellationToken ct) =>
        processes.RunAsync(Spec(["--terminate", distro], Timeouts.Query), ct);

    public static ProcessSpec ExecSpec(string distro, string user, IReadOnlyList<string> command, TimeSpan timeout) =>
        Spec(["-d", distro, "-u", user, "--exec", .. command], timeout);

    public static bool IsVirtualizationError(ProcessResult r) =>
        Contains(r, "HCS_E_HYPERV_NOT_INSTALLED") || Contains(r, "0x80370102");

    public static bool IsNetworkError(ProcessResult r) => Contains(r, "WININET_E_");

    private static bool Contains(ProcessResult r, string token) =>
        r.StdOut.Contains(token, StringComparison.Ordinal) || r.StdErr.Contains(token, StringComparison.Ordinal);

    private static ProcessSpec Spec(IReadOnlyList<string> args, TimeSpan timeout) => new(Exe, args, timeout, Env);
}
