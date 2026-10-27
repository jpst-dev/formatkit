using FormatKit.Core.Hardware;

namespace FormatKit.Drivers.Catalog;

internal static class GpuPackageSelector
{
    /// <summary>The catalog package for each detected GPU vendor, one per distinct winget ID.</summary>
    public static List<GpuPackage> Select(HardwareSnapshot hw, VendorCatalog catalog) =>
        hw.Gpus
            .Select(g => catalog.Gpu.FirstOrDefault(p => p.Vendor == g.Vendor))
            .OfType<GpuPackage>()
            .DistinctBy(p => p.WingetId)
            .ToList();
}
