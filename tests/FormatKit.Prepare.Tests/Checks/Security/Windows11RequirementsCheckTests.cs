using System.Globalization;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Prepare.Checks.Security;

namespace FormatKit.Prepare.Tests.Checks.Security;

public class Windows11RequirementsCheckTests : IDisposable
{
    private readonly CultureInfo _saved = CultureInfo.CurrentUICulture;

    public Windows11RequirementsCheckTests() => CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

    public void Dispose() => CultureInfo.CurrentUICulture = _saved;

    private static readonly ReadinessInfo Ready = new(19045, 16L << 30, true, true, 2);

    private static async Task<IReadOnlyList<PrepareItem>> RunAsync(ReadinessInfo info, bool elevated = true)
    {
        var context = PrepareTestContext.Create(TestServices.Create(readiness: new FakeWindowsReadiness(info)), isElevated: elevated);
        return await new Windows11RequirementsCheck().RunAsync(context, CancellationToken.None);
    }

    [Fact]
    public void Identity()
    {
        var check = new Windows11RequirementsCheck();
        Assert.Equal("security.windows11-requirements", check.Id);
        Assert.Equal(PrepareGroup.Security, check.Group);
    }

    [Fact]
    public async Task Windows11_is_a_single_ok()
    {
        var item = Assert.Single(await RunAsync(Ready with { Build = 22631, RamBytes = 1L << 30 }));
        Assert.Equal(PrepareStatus.Ok, item.Status);
        Assert.Equal("security.windows11-requirements", item.CheckId);
    }

    [Fact]
    public async Task Windows10_meeting_everything_is_ok_and_names_the_health_app()
    {
        var item = Assert.Single(await RunAsync(Ready));
        Assert.Equal(PrepareStatus.Ok, item.Status);
        Assert.Contains("PC Health Check", item.Action);
    }

    [Fact]
    public async Task Little_ram_needs_attention()
    {
        var item = Assert.Single(await RunAsync(Ready with { RamBytes = 2L << 30 }));
        Assert.Equal(PrepareStatus.Attention, item.Status);
        Assert.Contains("RAM", item.Title);
        Assert.Contains("PC Health Check", item.Action);
    }

    [Fact]
    public async Task Four_gib_machine_reporting_slightly_less_passes()
    {
        var item = Assert.Single(await RunAsync(Ready with { RamBytes = 4_100_000_000L - 300_000_000L }));
        Assert.Equal(PrepareStatus.Ok, item.Status);
    }

    [Fact]
    public async Task Legacy_firmware_needs_attention()
    {
        var item = Assert.Single(await RunAsync(Ready with { Uefi = false, SecureBoot = false }));
        Assert.Equal(PrepareStatus.Attention, item.Status);
        Assert.Contains("UEFI", item.Title);
        Assert.Contains("PC Health Check", item.Action);
    }

    [Fact]
    public async Task Secure_boot_off_needs_attention()
    {
        var item = Assert.Single(await RunAsync(Ready with { SecureBoot = false }));
        Assert.Equal(PrepareStatus.Attention, item.Status);
        Assert.Contains("Secure Boot", item.Title);
        Assert.Contains("PC Health Check", item.Action);
    }

    [Fact]
    public async Task Old_tpm_needs_attention()
    {
        var item = Assert.Single(await RunAsync(Ready with { TpmMajorVersion = 1 }));
        Assert.Equal(PrepareStatus.Attention, item.Status);
        Assert.Contains("TPM", item.Title);
        Assert.Contains("PC Health Check", item.Action);
    }

    [Fact]
    public async Task Missing_tpm_when_elevated_needs_attention()
    {
        var item = Assert.Single(await RunAsync(Ready with { TpmMajorVersion = null }));
        Assert.Equal(PrepareStatus.Attention, item.Status);
        Assert.Contains("TPM", item.Title);
    }

    [Fact]
    public async Task Unread_tpm_when_not_elevated_adds_a_not_checked_item_pointing_at_tpm_msc()
    {
        var items = await RunAsync(Ready with { TpmMajorVersion = null }, elevated: false);
        var item = Assert.Single(items);
        Assert.Equal(PrepareStatus.NotChecked, item.Status);
        Assert.Contains("tpm.msc", item.Action);
        Assert.Contains("PC Health Check", item.Action);
    }

    [Fact]
    public async Task Not_elevated_with_other_failures_reports_both()
    {
        var items = await RunAsync(Ready with { RamBytes = 2L << 30, TpmMajorVersion = null }, elevated: false);
        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.Status == PrepareStatus.Attention);
        Assert.Contains(items, i => i.Status == PrepareStatus.NotChecked);
        Assert.All(items, i => Assert.Contains("PC Health Check", i.Action));
    }

    [Fact]
    public async Task Unreadable_values_are_not_checked()
    {
        var item = Assert.Single(await RunAsync(new ReadinessInfo(19045, null, null, null, 2)));
        Assert.Equal(PrepareStatus.NotChecked, item.Status);
        Assert.Contains("PC Health Check", item.Action);
    }
}
