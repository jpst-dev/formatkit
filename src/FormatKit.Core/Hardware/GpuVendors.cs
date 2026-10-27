namespace FormatKit.Core.Hardware;

public static class GpuVendors
{
    public static GpuVendor FromPnpId(string pnpDeviceId)
    {
        if (string.IsNullOrEmpty(pnpDeviceId)) return GpuVendor.Unknown;
        if (pnpDeviceId.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Nvidia;
        if (pnpDeviceId.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Amd;
        if (pnpDeviceId.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Intel;
        return GpuVendor.Unknown;
    }
}
