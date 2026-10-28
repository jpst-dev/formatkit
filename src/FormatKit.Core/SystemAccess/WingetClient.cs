using System.Text.RegularExpressions;

namespace FormatKit.Core.SystemAccess;

public enum WingetStatus
{
    Installed,
    AlreadyInstalled,
    Upgraded,
    UpToDate,
    RebootRequired,
    NotFound,
    NoNetwork,
    BlockedByPolicy,
    Failed,
    WingetMissing,
}

public sealed record WingetResult(WingetStatus Status, int ExitCode, string Output, bool TimedOut = false);

/// <summary>A package to install or upgrade. <paramref name="Scope"/>, when set, is "machine" or "user" and is passed as --scope.</summary>
public sealed record WingetPackageRef(string Id, string? Source = null, string? Version = null, string? Scope = null);

public partial class WingetClient(IProcessRunner runner)
{
    // Values confirmed against microsoft/winget-cli doc/windows/package-manager/winget/returnCodes.md.
    private const int UpdateNotApplicable = unchecked((int)0x8A15002B);        // UPDATE_NOT_APPLICABLE
    private const int PackageAlreadyInstalled = unchecked((int)0x8A150061);    // PACKAGE_ALREADY_INSTALLED
    private const int NoApplicationsFound = unchecked((int)0x8A150014);        // NO_APPLICATIONS_FOUND
    private const int RebootRequiredToFinish = unchecked((int)0x8A150109);     // INSTALL_REBOOT_REQUIRED_TO_FINISH
    private const int RebootRequiredForInstall = unchecked((int)0x8A15010A);   // INSTALL_REBOOT_REQUIRED_FOR_INSTALL
    private const int DownloadFailed = unchecked((int)0x8A150008);             // DOWNLOAD_FAILED
    private const int SourceDataMissing = unchecked((int)0x8A15000F);          // SOURCE_DATA_MISSING
    private const int SourceOpenFailed = unchecked((int)0x8A150045);           // SOURCE_OPEN_FAILED
    private const int FailedToOpenAllSources = unchecked((int)0x8A15004B);     // FAILED_TO_OPEN_ALL_SOURCES
    private const int InstallerZeroByteFile = unchecked((int)0x8A150086);      // INSTALLER_ZERO_BYTE_FILE
    private const int InstallNoNetwork = unchecked((int)0x8A150107);           // INSTALL_NO_NETWORK
    private const int MsStoreBlockedByPolicy = unchecked((int)0x8A15001B);     // MSSTORE_BLOCKED_BY_POLICY
    private const int MsStoreAppBlockedByPolicy = unchecked((int)0x8A15001C);  // MSSTORE_APP_BLOCKED_BY_POLICY
    private const int BlockedByPolicy = unchecked((int)0x8A15003A);            // BLOCKED_BY_POLICY
    private const int InstallBlockedByPolicy = unchecked((int)0x8A15010F);     // INSTALL_BLOCKED_BY_POLICY

    private static readonly string[] SilentFlags =
        ["--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"];

    [GeneratedRegex(@"\A[A-Za-z0-9._+-]+\z")]
    private static partial Regex SafeValue();

    public static string Validate(string value, string what)
    {
        if (value is null || !SafeValue().IsMatch(value) || value.StartsWith('-'))
        {
            throw new ArgumentException($"Unsafe {what}: '{value}'.", what);
        }

        return value;
    }

    public static WingetStatus MapExitCode(int exitCode, bool upgrade, bool timedOut)
    {
        if (timedOut)
        {
            return WingetStatus.Failed;
        }

        return exitCode switch
        {
            0 => upgrade ? WingetStatus.Upgraded : WingetStatus.Installed,
            UpdateNotApplicable => upgrade ? WingetStatus.UpToDate : WingetStatus.AlreadyInstalled,
            PackageAlreadyInstalled => WingetStatus.AlreadyInstalled,
            NoApplicationsFound => WingetStatus.NotFound,
            RebootRequiredToFinish or RebootRequiredForInstall => WingetStatus.RebootRequired,
            DownloadFailed or SourceDataMissing or SourceOpenFailed or FailedToOpenAllSources
                or InstallerZeroByteFile or InstallNoNetwork => WingetStatus.NoNetwork,
            MsStoreBlockedByPolicy or MsStoreAppBlockedByPolicy or BlockedByPolicy or InstallBlockedByPolicy
                => WingetStatus.BlockedByPolicy,
            _ => WingetStatus.Failed,
        };
    }

    /// <summary>
    /// Install arguments. <c>--no-upgrade</c> stops winget (1.6 and later) from silently upgrading a package that is
    /// already installed; winget then exits with PACKAGE_ALREADY_INSTALLED, which maps to AlreadyInstalled.
    /// </summary>
    public static IReadOnlyList<string> InstallArguments(WingetPackageRef package) =>
        Build("install", package, [.. SilentFlags, "--no-upgrade"], withScope: true);

    public static IReadOnlyList<string> UpgradeArguments(WingetPackageRef package) =>
        Build("upgrade", package, SilentFlags, withScope: true);

    public Task<WingetResult> InstallAsync(string id, CancellationToken ct, string? source = null) =>
        InstallAsync(new WingetPackageRef(id, source), ct);

    public Task<WingetResult> InstallAsync(WingetPackageRef package, CancellationToken ct) =>
        RunMutatingAsync(InstallArguments(package), upgrade: false, ct);

    public Task<WingetResult> UpgradeAsync(WingetPackageRef package, CancellationToken ct) =>
        RunMutatingAsync(UpgradeArguments(package), upgrade: true, ct);

    public Task<bool> IsInstalledAsync(string id, CancellationToken ct, string? source = null) =>
        IsInstalledAsync(new WingetPackageRef(id, source), ct);

    public async Task<bool> IsInstalledAsync(WingetPackageRef package, CancellationToken ct)
    {
        // winget list has no --version option (and Scope is install-only), so neither is passed.
        var args = Build("list", package with { Version = null }, ["--accept-source-agreements", "--disable-interactivity"]);
        try
        {
            var result = await runner.RunAsync(new ProcessSpec("winget.exe", args, Timeouts.Query), ct)
                .ConfigureAwait(false);
            return !result.TimedOut && result.ExitCode == 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        try
        {
            var result = await runner.RunAsync(new ProcessSpec("winget.exe", ["--version"], Timeouts.Query), ct)
                .ConfigureAwait(false);
            return !result.TimedOut && result.ExitCode == 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    public Task<ProcessResult> SearchAsync(string term, CancellationToken ct) =>
        runner.RunAsync(
            new ProcessSpec(
                "winget.exe",
                ["search", "--query", term, "--accept-source-agreements", "--disable-interactivity"],
                Timeouts.Query),
            ct);

    public async Task<WingetResult> ExportAsync(string outputFile, CancellationToken ct)
    {
        ProcessResult result;
        try
        {
            result = await runner.RunAsync(
                new ProcessSpec(
                    "winget.exe",
                    ["export", "-o", outputFile, "--accept-source-agreements", "--disable-interactivity"],
                    Timeouts.Query),
                ct).ConfigureAwait(false);
        }
        catch (FileNotFoundException ex)
        {
            return new WingetResult(WingetStatus.WingetMissing, -1, ex.Message);
        }

        var status = result.TimedOut || result.ExitCode != 0 ? WingetStatus.Failed : WingetStatus.Installed;
        return new WingetResult(status, result.ExitCode, result.StdOut + result.StdErr, result.TimedOut);
    }

    private async Task<WingetResult> RunMutatingAsync(IReadOnlyList<string> args, bool upgrade, CancellationToken ct)
    {
        ProcessResult result;
        try
        {
            result = await runner.RunAsync(new ProcessSpec("winget.exe", args, Timeouts.Install), ct)
                .ConfigureAwait(false);
        }
        catch (FileNotFoundException ex)
        {
            return new WingetResult(WingetStatus.WingetMissing, -1, ex.Message);
        }

        return new WingetResult(
            MapExitCode(result.ExitCode, upgrade, result.TimedOut),
            result.ExitCode,
            result.StdOut + result.StdErr,
            result.TimedOut);
    }

    private static string[] Build(string verb, WingetPackageRef package, string[] tail, bool withScope = false)
    {
        List<string> args = [verb, "--id", Validate(package.Id, "id"), "--exact", .. tail];
        if (package.Source is not null)
        {
            args.Add("--source");
            args.Add(Validate(package.Source, "source"));
        }

        if (package.Version is not null)
        {
            args.Add("--version");
            args.Add(Validate(package.Version, "version"));
        }

        if (withScope && package.Scope is not null)
        {
            if (package.Scope is not ("machine" or "user"))
                throw new ArgumentException($"Winget scope must be 'machine' or 'user', not '{package.Scope}'.", nameof(package));
            args.Add("--scope");
            args.Add(package.Scope);
        }

        return [.. args];
    }
}
