namespace FormatKit.Core.Hardware;

public interface IHardwareInfo
{
    Task<HardwareSnapshot> ReadAsync(CancellationToken ct);
}
