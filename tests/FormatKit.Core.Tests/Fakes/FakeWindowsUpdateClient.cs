using FormatKit.Core.WindowsUpdate;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeWindowsUpdateClient : IWindowsUpdateClient
{
    public List<DriverUpdate> Updates { get; } = [];
    public WindowsUpdateInstallResult InstallResult { get; set; } = new(true, false, 0, []);
    public int SearchCount { get; private set; }
    public List<IReadOnlyList<DriverUpdate>> Installed { get; } = [];

    public Task<IReadOnlyList<DriverUpdate>> SearchDriversAsync(CancellationToken ct)
    {
        SearchCount++;
        return Task.FromResult<IReadOnlyList<DriverUpdate>>(Updates.ToList());
    }

    public Task<WindowsUpdateInstallResult> InstallAsync(IReadOnlyList<DriverUpdate> updates, CancellationToken ct)
    {
        Installed.Add(updates);
        return Task.FromResult(InstallResult);
    }
}
