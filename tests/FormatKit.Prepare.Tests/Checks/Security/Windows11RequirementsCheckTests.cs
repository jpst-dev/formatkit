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
    public async Task Absent_tpm_when_elevated_needs_attention()
    {
        var item = Assert.Single(await RunAsync(Ready with { TpmMajorVersion = null, TpmAbsent = true }));
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

    [Fact]
    public async Task Unreadable_tpm_when_elevated_is_not_checked_not_missing()
    {
        var item = Assert.Single(await RunAsync(Ready with { TpmMajorVersion = null }, elevated: true));
        Assert.Equal(PrepareStatus.NotChecked, item.Status);
        Assert.DoesNotContain("No TPM", item.Title);
        Assert.Contains("tpm.msc", item.Action);
        Assert.Contains("PC Health Check", item.Action);
        Assert.DoesNotContain("administrator", item.Detail);
    }

    [Fact]
    public async Task Ram_limit_is_inclusive_at_3_5_gib()
    {
        var limit = 3584L << 20;
        Assert.Equal(PrepareStatus.Ok, Assert.Single(await RunAsync(Ready with { RamBytes = limit })).Status);
        var below = Assert.Single(await RunAsync(Ready with { RamBytes = limit - 1 }));
        Assert.Equal(PrepareStatus.Attention, below.Status);
        Assert.Contains("RAM", below.Title);
    }

    public static TheoryData<ReadinessInfo> FailingCases => new()
    {
        Ready with { RamBytes = 2L << 30 },
        Ready with { Uefi = false },
        Ready with { SecureBoot = false },
        Ready with { TpmMajorVersion = 1 },
        Ready with { TpmMajorVersion = null, TpmAbsent = true },
    };

    [Theory]
    [MemberData(nameof(FailingCases))]
    public async Task Every_attention_says_windows_11_will_not_install_and_media_does_not_bypass(ReadinessInfo info)
    {
        var item = Assert.Single(await RunAsync(info));
        Assert.Equal(PrepareStatus.Attention, item.Status);
        Assert.Contains("Windows 11 will not install on this PC as it is", item.Detail);
        Assert.Contains("does not bypass the requirements", item.Detail);
        Assert.Contains("processor", item.Action);
    }

    [Fact]
    public async Task Met_names_the_processor_in_detail_and_action_in_english()
    {
        var item = Assert.Single(await RunAsync(Ready));
        Assert.Contains("processor", item.Detail);
        Assert.Contains("FormatKit does not check the processor", item.Action);
        Assert.Contains("PC Health Check", item.Action);
    }

    [Fact]
    public async Task Met_names_the_processor_in_detail_and_action_in_portuguese()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("pt-BR");
        var item = Assert.Single(await RunAsync(Ready));
        Assert.Contains("processador", item.Detail);
        Assert.Contains("processador", item.Action);
        Assert.Contains("Verificação de Integridade do PC", item.Action);
    }

    [Fact]
    public async Task Unknown_values_are_named()
    {
        var item = Assert.Single(await RunAsync(new ReadinessInfo(19045, null, true, null, 2)));
        Assert.Equal(PrepareStatus.NotChecked, item.Status);
        Assert.Contains("memory", item.Detail);
        Assert.Contains("Secure Boot", item.Detail);
        Assert.DoesNotContain("firmware type", item.Detail);
    }
}
