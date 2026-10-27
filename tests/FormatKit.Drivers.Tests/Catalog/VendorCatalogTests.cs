using FormatKit.Core.Hardware;
using FormatKit.Drivers.Catalog;

namespace FormatKit.Drivers.Tests.Catalog;

public class VendorCatalogTests
{
    [Fact]
    public void Embedded_catalog_loads_and_has_three_oems()
    {
        var catalog = VendorCatalog.LoadEmbedded();
        Assert.Equal(["dell", "lenovo", "hp"], catalog.Oem.Select(o => o.Id));
    }

    [Fact]
    public void Every_oem_has_wingetId_executable_and_success_code()
    {
        foreach (var oem in VendorCatalog.LoadEmbedded().Oem)
        {
            Assert.False(string.IsNullOrWhiteSpace(oem.WingetId), oem.Id);
            Assert.False(string.IsNullOrWhiteSpace(oem.Executable), oem.Id);
            Assert.NotEmpty(oem.SuccessExitCodes);
            Assert.NotEmpty(oem.ManufacturerMatches);
        }
    }

    [Fact]
    public void Every_gpu_vendor_except_unknown_has_a_package()
    {
        var catalog = VendorCatalog.LoadEmbedded();
        foreach (var vendor in Enum.GetValues<GpuVendor>().Where(v => v != GpuVendor.Unknown))
            Assert.Contains(catalog.Gpu, g => g.Vendor == vendor && g.WingetId.Length > 0);
    }

    [Fact]
    public void Parse_reads_camelCase_json_with_string_gpu_vendors()
    {
        var catalog = VendorCatalog.Parse("""
            {"oem":[],"gpu":[{"vendor":"amd","wingetId":"X.Y"}],"boards":[{"manufacturerMatches":["A"],"supportUrl":"https://a"}]}
            """);
        Assert.Equal(GpuVendor.Amd, Assert.Single(catalog.Gpu).Vendor);
        Assert.Equal("https://a", Assert.Single(catalog.Boards).SupportUrl);
    }
}
