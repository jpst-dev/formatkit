using FormatKit.Core.Hardware;

namespace FormatKit.Core.Tests.Hardware;

public class GpuVendorsTests
{
    [Theory]
    [InlineData("PCI\\VEN_10DE&DEV_2684", GpuVendor.Nvidia)]
    [InlineData("pci\\ven_1002&dev_744c", GpuVendor.Amd)]
    [InlineData("PCI\\VEN_8086&DEV_A780", GpuVendor.Intel)]
    [InlineData("ROOT\\BasicDisplay", GpuVendor.Unknown)]
    [InlineData("", GpuVendor.Unknown)]
    public void FromPnpId_maps_vendor(string id, GpuVendor expected)
        => Assert.Equal(expected, GpuVendors.FromPnpId(id));
}
