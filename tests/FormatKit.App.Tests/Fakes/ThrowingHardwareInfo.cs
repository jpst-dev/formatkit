using System.Runtime.InteropServices;
using FormatKit.Core.Hardware;
using FormatKit.Core.SystemAccess;

namespace FormatKit.App.Tests.Fakes;

/// <summary>A hardware reader whose query fails the way a refused WMI/COM call does.</summary>
internal sealed class ThrowingHardwareInfo : IHardwareInfo
{
    public Task<HardwareSnapshot> ReadAsync(CancellationToken ct) => throw new COMException("access refused");

    public Task<IReadOnlyList<ProblemDevice>> ReadDisabledDevicesAsync(CancellationToken ct) => throw new COMException("access refused");

    public Task<bool> HasBatteryAsync(CancellationToken ct) => throw new COMException("access refused");
}
