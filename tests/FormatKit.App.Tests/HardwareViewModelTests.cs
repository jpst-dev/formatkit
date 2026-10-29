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

    [Fact]
    public async Task Scan_DuringLoad_KeepsBothResults()
    {
        var hardware = new GatedHardwareInfo();
        var vm = new HardwareViewModel(
            TestServices.Create() with { Hardware = hardware }, VendorCatalog.LoadEmbedded());

        var load = vm.LoadCommand.ExecuteAsync(null);
        // Queries start on the thread pool: wait until the first one holds the gate.
        await hardware.FirstStarted.WaitAsync(TestContext.Current.CancellationToken);
        await vm.ScanCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        hardware.Release();
        await load;

        Assert.NotNull(vm.Scan);
        Assert.NotNull(vm.Hardware);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Load_Twice_AppliesOnlyTheSecondResult()
    {
        var hardware = new GatedHardwareInfo();
        var vm = new HardwareViewModel(
            TestServices.Create() with { Hardware = hardware }, VendorCatalog.LoadEmbedded());

        var first = vm.LoadCommand.ExecuteAsync(null);
        await hardware.FirstStarted.WaitAsync(TestContext.Current.CancellationToken);
        await vm.LoadCommand.ExecuteAsync(null);
        hardware.Release();
        await first;

        Assert.Equal("Second Model 1", vm.MachineText);
    }

    /// <summary>The first read waits for <see cref="Release"/> and ignores cancellation; later reads return at once.</summary>
    private sealed class GatedHardwareInfo : IHardwareInfo
    {
        private readonly TaskCompletionSource _gate = new();
        private readonly TaskCompletionSource _firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        /// <summary>Completes when the first read is waiting at the gate.</summary>
        public Task FirstStarted => _firstStarted.Task;

        public void Release() => _gate.SetResult();

        public async Task<HardwareSnapshot> ReadAsync(CancellationToken ct)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                _firstStarted.SetResult();
                await _gate.Task;
                return FakeHardwareInfo.Sample(manufacturer: "First");
            }

            return FakeHardwareInfo.Sample(manufacturer: "Second");
        }

        public Task<IReadOnlyList<ProblemDevice>> ReadDisabledDevicesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ProblemDevice>>([]);

        public Task<bool> HasBatteryAsync(CancellationToken ct) => Task.FromResult(false);
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
