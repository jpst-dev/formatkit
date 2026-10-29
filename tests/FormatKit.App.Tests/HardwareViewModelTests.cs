using FormatKit.App.Tests.Fakes;
using FormatKit.App.ViewModels;
using FormatKit.Core.Hardware;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Drivers.Catalog;

namespace FormatKit.App.Tests;

public class HardwareViewModelTests
{
    private static HardwareViewModel Create(FakeHardwareInfo? hardware = null) =>
        new(TestServices.Create(hardware: hardware), VendorCatalog.LoadEmbedded());

    [Fact]
    public async Task Load_FillsSnapshot()
    {
        var snapshot = FakeHardwareInfo.Sample(
            gpus: [new GpuInfo("Card", @"PCIX", GpuVendor.Nvidia, "1.2")]);
        var vm = Create(new FakeHardwareInfo(snapshot));

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Same(snapshot, vm.Hardware);
        Assert.Equal("Contoso Model 1", vm.MachineText);
        Assert.Contains("Card (driver 1.2)", vm.GpuLines);
        Assert.Null(vm.Error);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Load_ComException_SetsErrorAndKeepsPageAlive()
    {
        var services = TestServices.Create();
        var vm = new HardwareViewModel(
            services with { Hardware = new ThrowingHardwareInfo() }, VendorCatalog.LoadEmbedded());

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.NotNull(vm.Error);
        Assert.Contains("access refused", vm.Error);
        Assert.Null(vm.Hardware);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Scan_FillsReport()
    {
        var vm = Create();

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.NotNull(vm.Scan);
        Assert.True(vm.HasScan);
        Assert.Equal("Contoso Model 1", vm.Scan.Hardware.Machine.Manufacturer + " " + vm.Scan.Hardware.Machine.Model);
        Assert.NotEmpty(vm.MachineTypeText);
    }

    [Fact]
    public async Task Leave_CancelsRunningQuery()
    {
        var hardware = new BlockingHardwareInfo();
        var vm = new HardwareViewModel(
            TestServices.Create() with { Hardware = hardware }, VendorCatalog.LoadEmbedded());

        var load = vm.LoadCommand.ExecuteAsync(null);
        await hardware.Started.Task;
        vm.Leave();
        await load;

        Assert.True(hardware.Token.IsCancellationRequested);
        Assert.Null(vm.Error);
        Assert.False(vm.IsBusy);
    }

    private sealed class BlockingHardwareInfo : IHardwareInfo
    {
        public TaskCompletionSource Started { get; } = new();
        public CancellationToken Token { get; private set; }

        public async Task<HardwareSnapshot> ReadAsync(CancellationToken ct)
        {
            Token = ct;
            Started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return FakeHardwareInfo.Sample();
        }

        public Task<IReadOnlyList<ProblemDevice>> ReadDisabledDevicesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ProblemDevice>>([]);

        public Task<bool> HasBatteryAsync(CancellationToken ct) => Task.FromResult(false);
    }
}
