using FormatKit.Core.Hardware;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeHardwareInfo(HardwareSnapshot snapshot) : IHardwareInfo
{
    public HardwareSnapshot Snapshot { get; set; } = snapshot;
    public int ReadCount { get; private set; }

    public Task<HardwareSnapshot> ReadAsync(CancellationToken ct)
    {
        ReadCount++;
        return Task.FromResult(Snapshot);
    }

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
