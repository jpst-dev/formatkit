using FormatKit.Core.Hardware;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Drivers.Tests;

public class NetworkDriverBackupTests
{
    private const string Out = @"C:\out";

    private static ProcessResult Exit(int code) => new(code, "", "", TimeSpan.Zero, false);

    private static (NetworkDriverBackup Backup, FakeProcessRunner Procs, InMemoryFileSystem Fs) Arrange(
        params NetworkAdapterInfo[] adapters)
    {
        var procs = new FakeProcessRunner();
        var fs = new InMemoryFileSystem();
        var hw = new FakeHardwareInfo(FakeHardwareInfo.Sample(networkAdapters: adapters));
        return (new NetworkDriverBackup(TestServices.Create(processes: procs, hardware: hw, files: fs)), procs, fs);
    }

    [Fact]
    public async Task Exports_only_oem_infs()
    {
        var (backup, procs, fs) = Arrange(
            new NetworkAdapterInfo("Realtek: PCIe GbE", "PCI\\A", "oem12.inf", "1.0"),
            new NetworkAdapterInfo("Intel Wi-Fi", "PCI\\B", "netwtw10.inf", "2.0"));
        procs.Enqueue("pnputil.exe", Exit(0));

        var result = await backup.ExportAsync(Out, CancellationToken.None);

        Assert.Single(result.Exported);
        Assert.Single(result.SkippedInbox);
        Assert.Empty(result.Failed);
        Assert.True(fs.DirectoryExists(Out));
        var call = Assert.Single(procs.Calls);
        Assert.Contains("oem12.inf", call.Arguments);
        Assert.Contains(Path.Combine(Out, "Realtek_ PCIe GbE"), call.Arguments);
    }

    [Fact]
    public async Task Export_failure_is_reported()
    {
        var (backup, procs, _) = Arrange(new NetworkAdapterInfo("Realtek", "PCI\\A", "OEM3.INF", "1.0"));
        procs.Enqueue("pnputil.exe", Exit(1));

        var result = await backup.ExportAsync(Out, CancellationToken.None);

        Assert.Empty(result.Exported);
        Assert.Equal(["Realtek"], result.Failed);
    }

    [Fact]
    public async Task Colliding_sanitized_names_get_unique_folders()
    {
        var (backup, procs, _) = Arrange(
            new NetworkAdapterInfo("A/B", "PCI\\A", "oem1.inf", "1"),
            new NetworkAdapterInfo("A?B", "PCI\\B", "oem2.inf", "1"));
        procs.Enqueue("pnputil.exe", Exit(0));
        procs.Enqueue("pnputil.exe", Exit(0));

        await backup.ExportAsync(Out, CancellationToken.None);

        Assert.Contains(Path.Combine(Out, "A_B"), procs.Calls[0].Arguments);
        Assert.Contains(Path.Combine(Out, "A_B (2)"), procs.Calls[1].Arguments);
    }
}
