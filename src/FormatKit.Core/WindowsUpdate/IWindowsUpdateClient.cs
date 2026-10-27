namespace FormatKit.Core.WindowsUpdate;

public sealed record DriverUpdate(string UpdateId, string Title, string? DriverHardwareId);

public sealed record WindowsUpdateInstallResult(
    bool Succeeded,
    bool RebootRequired,
    int HResult,
    IReadOnlyList<string> FailedTitles,
    IReadOnlyList<string>? InstalledTitles = null);

public interface IWindowsUpdateClient
{
    Task<IReadOnlyList<DriverUpdate>> SearchDriversAsync(CancellationToken ct);

    Task<WindowsUpdateInstallResult> InstallAsync(IReadOnlyList<DriverUpdate> updates, CancellationToken ct);
}
