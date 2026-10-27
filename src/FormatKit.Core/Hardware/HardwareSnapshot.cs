namespace FormatKit.Core.Hardware;

public enum GpuVendor { Nvidia, Amd, Intel, Unknown }

public sealed record MachineInfo(string Manufacturer, string Model, string SystemFamily);

public sealed record BoardInfo(string Manufacturer, string Product);

public sealed record GpuInfo(string Name, string PnpDeviceId, GpuVendor Vendor, string? DriverVersion);

public sealed record ProblemDevice(string Name, string DeviceId, int ErrorCode, IReadOnlyList<string> HardwareIds);

public sealed record NetworkAdapterInfo(string Name, string PnpDeviceId, string? InfName, string? DriverVersion);

public sealed record HardwareSnapshot(
    MachineInfo Machine,
    BoardInfo Board,
    string BiosVersion,
    IReadOnlyList<GpuInfo> Gpus,
    IReadOnlyList<ProblemDevice> ProblemDevices,
    IReadOnlyList<NetworkAdapterInfo> NetworkAdapters);
