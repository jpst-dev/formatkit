namespace FormatKit.Core.Hardware;

public interface IHardwareInfo
{
    Task<HardwareSnapshot> ReadAsync(CancellationToken ct);
    Task<IReadOnlyList<ProblemDevice>> ReadDisabledDevicesAsync(CancellationToken ct);
    Task<bool> HasBatteryAsync(CancellationToken ct);
}
