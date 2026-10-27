using FormatKit.Core.Hardware;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Core.WindowsUpdate;
using FormatKit.Drivers.Catalog;

namespace FormatKit.Drivers.Tests;

public class DriverScannerTests
{
    private static readonly VendorCatalog Catalog = VendorCatalog.LoadEmbedded();

    private static HardwareSnapshot DellWithNvidia() => FakeHardwareInfo.Sample(
        manufacturer: "Dell Inc.",
        gpus: [new GpuInfo("RTX", "PCI\\VEN_10DE&DEV_1", GpuVendor.Nvidia, null)]);

    [Fact]
    public async Task Scan_reports_machine_updates_and_gpu_packages()
    {
        var wu = new FakeWindowsUpdateClient();
        wu.Updates.Add(new DriverUpdate("u1", "Some driver", "PCI\\VEN_1"));
        var services = TestServices.Create(hardware: new FakeHardwareInfo(DellWithNvidia()), wu: wu);

        var report = await new DriverScanner(services, Catalog).ScanAsync(CancellationToken.None);

        Assert.True(report.Machine.IsOem);
        Assert.Single(report.AvailableUpdates);
        var package = Assert.Single(report.GpuPackages);
        Assert.Equal(GpuVendor.Nvidia, package.Vendor);
        Assert.True(report.Online);
        Assert.Null(report.WindowsUpdateError);
    }

    [Fact]
    public async Task Scan_reports_offline()
    {
        var services = TestServices.Create(network: new FakeNetworkProbe(false));

        var report = await new DriverScanner(services, Catalog).ScanAsync(CancellationToken.None);

        Assert.False(report.Online);
    }

    [Fact]
    public async Task Scan_never_installs()
    {
        var wu = new FakeWindowsUpdateClient();
        wu.Updates.Add(new DriverUpdate("u1", "Some driver", null));
        var runner = new FakeProcessRunner();
        var services = TestServices.Create(runner, new FakeHardwareInfo(DellWithNvidia()), wu);

        await new DriverScanner(services, Catalog).ScanAsync(CancellationToken.None);

        Assert.Empty(wu.Installed);
        Assert.DoesNotContain(runner.Calls, c => string.Join(' ', c.Arguments).Contains("install", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Scan_survives_windows_update_failure()
    {
        var services = TestServices.Create() with { WindowsUpdate = new ThrowingWindowsUpdateClient() };

        var report = await new DriverScanner(services, Catalog).ScanAsync(CancellationToken.None);

        Assert.Empty(report.AvailableUpdates);
        Assert.Equal("COM unavailable", report.WindowsUpdateError);
    }

    private sealed class ThrowingWindowsUpdateClient : IWindowsUpdateClient
    {
        public Task<IReadOnlyList<DriverUpdate>> SearchDriversAsync(CancellationToken ct) =>
            throw new InvalidOperationException("COM unavailable");

        public Task<WindowsUpdateInstallResult> InstallAsync(IReadOnlyList<DriverUpdate> updates, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
