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
    public void Nvidia_and_intel_have_packages_and_amd_has_none_because_it_uses_windows_update()
    {
        var catalog = VendorCatalog.LoadEmbedded();
        Assert.Contains(catalog.Gpu, g => g.Vendor == GpuVendor.Nvidia && g.WingetId.Length > 0);
        Assert.Contains(catalog.Gpu, g => g.Vendor == GpuVendor.Intel && g.WingetId.Length > 0);
        Assert.DoesNotContain(catalog.Gpu, g => g.Vendor == GpuVendor.Amd);
    }

    [Fact]
    public void Nvidia_uses_msstore_source_and_intel_uses_default()
    {
        var catalog = VendorCatalog.LoadEmbedded();
        Assert.Equal("msstore", catalog.Gpu.Single(g => g.Vendor == GpuVendor.Nvidia).Source);
        Assert.Null(catalog.Gpu.Single(g => g.Vendor == GpuVendor.Intel).Source);
    }

    [Fact]
    public void Hp_executable_points_at_default_swsetup_location()
        => Assert.Equal(@"C:SWSetupHPImageAssistantHPImageAssistant.exe", VendorCatalog.LoadEmbedded().Oem.Single(o => o.Id == "hp").Executable);

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
