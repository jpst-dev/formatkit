using System.Globalization;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Prepare.Checks.Security;

namespace FormatKit.Prepare.Tests.Checks.Security;

public class ActivationCheckTests : IDisposable
{
    private readonly CultureInfo _saved = CultureInfo.CurrentUICulture;

    public ActivationCheckTests() => CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

    public void Dispose() => CultureInfo.CurrentUICulture = _saved;

    private static async Task<PrepareItem> RunAsync(WindowsActivation? activation)
    {
        var context = PrepareTestContext.Create(TestServices.Create(licensing: new FakeLicensing(activation)));
        var items = await new ActivationCheck().RunAsync(context, CancellationToken.None);
        return Assert.Single(items);
    }

    [Fact]
    public void Identity()
    {
        var check = new ActivationCheck();
        Assert.Equal("security.activation", check.Id);
        Assert.Equal(PrepareGroup.Security, check.Group);
    }

    [Fact]
    public async Task Firmware_key_is_ok()
    {
        var item = await RunAsync(new WindowsActivation(true, "OEM:DM"));
        Assert.Equal(PrepareStatus.Ok, item.Status);
        Assert.Equal("security.activation", item.CheckId);
    }

    [Theory]
    [InlineData("Retail")]
    [InlineData("OEM:NONSLP")]
    [InlineData(null)]
    public async Task Retail_and_other_channels_point_at_the_activation_settings(string? channel)
    {
        var item = await RunAsync(new WindowsActivation(true, channel));
        Assert.Equal(PrepareStatus.Attention, item.Status);
        Assert.Contains("Settings > System > Activation", item.Action);
    }

    [Fact]
    public async Task Volume_license_points_at_the_organization()
    {
        var item = await RunAsync(new WindowsActivation(true, "Volume:MAK"));
        Assert.Equal(PrepareStatus.Attention, item.Status);
        Assert.Contains("organization", item.Detail + item.Action + item.Title);
        Assert.DoesNotContain("Settings > System > Activation", item.Action);
    }

    [Fact]
    public async Task Not_activated_needs_a_product_key()
    {
        var item = await RunAsync(new WindowsActivation(false, "Retail"));
        Assert.Equal(PrepareStatus.Attention, item.Status);
        Assert.Contains("not activated", item.Title);
        Assert.Contains("product key", item.Action);
    }

    [Fact]
    public async Task Unreadable_activation_is_not_checked()
    {
        var item = await RunAsync(null);
        Assert.Equal(PrepareStatus.NotChecked, item.Status);
    }
}
