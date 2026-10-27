using FormatKit.Core.Hardware;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeHardwareInfo(HardwareSnapshot snapshot) : IHardwareInfo
{
    public HardwareSnapshot Snapshot { get; set; } = snapshot;
    public IReadOnlyList<ProblemDevice> DisabledDevices { get; set; } = [];
    public bool HasBattery { get; set; }
    public int ReadCount { get; private set; }

    public Task<HardwareSnapshot> ReadAsync(CancellationToken ct)
    {
        ReadCount++;
        return Task.FromResult(Snapshot);
    }

    public Task<IReadOnlyList<ProblemDevice>> ReadDisabledDevicesAsync(CancellationToken ct) =>
        Task.FromResult(DisabledDevices);

    public Task<bool> HasBatteryAsync(CancellationToken ct) => Task.FromResult(HasBattery);

    public static HardwareSnapshot Sample(
        string manufacturer = "Contoso",
        string model = "Model 1",
        string boardManufacturer = "Contoso",
        IReadOnlyList<GpuInfo>? gpus = null,
        IReadOnlyList<ProblemDevice>? problemDevices = null,
        IReadOnlyList<NetworkAdapterInfo>? networkAdapters = null)
        => new(
            new MachineInfo(manufacturer, model, ""),
            new BoardInfo(boardManufacturer, "Board 1"),
            "1.0",
            gpus ?? [],
            problemDevices ?? [],
            networkAdapters ?? []);
}
