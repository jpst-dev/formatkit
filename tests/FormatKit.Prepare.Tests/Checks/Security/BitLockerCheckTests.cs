using System.Globalization;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Prepare.Checks.Security;

namespace FormatKit.Prepare.Tests.Checks.Security;

public class BitLockerCheckTests : IDisposable
{
    private readonly CultureInfo _saved = CultureInfo.CurrentUICulture;

    public BitLockerCheckTests() => CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

    public void Dispose() => CultureInfo.CurrentUICulture = _saved;

    private static VolumeInfo Vol(char letter, BitLockerState state, bool? recovery = null) =>
        new(letter, 0, state, null, recovery);

    private static async Task<IReadOnlyList<PrepareItem>> RunAsync(bool elevated, params VolumeInfo[] volumes)
    {
        var context = PrepareTestContext.Create(
            TestServices.Create(volumes: new FakeVolumes(volumes)),
            volumes,
            elevated);
        return await new BitLockerCheck().RunAsync(context, CancellationToken.None);
    }

    [Fact]
    public void Identity()
    {
        var check = new BitLockerCheck();
        Assert.Equal("security.bitlocker", check.Id);
        Assert.Equal(PrepareGroup.Security, check.Group);
    }

    [Fact]
    public async Task Windows_volume_encrypted_is_ok_and_says_encryption_returns()
    {
        var items = await RunAsync(false, Vol('C', BitLockerState.On));

        var item = Assert.Single(items);
        Assert.Equal(PrepareStatus.Ok, item.Status);
        Assert.Equal("security.bitlocker", item.CheckId);
        Assert.Contains("device encryption", item.Detail);
    }

    [Fact]
    public async Task Windows_volume_off_has_no_item()
    {
        Assert.Empty(await RunAsync(false, Vol('C', BitLockerState.Off)));
    }

    [Fact]
    public async Task Encrypted_other_volume_not_elevated_gives_attention_and_not_checked()
    {
        var items = await RunAsync(false, Vol('C', BitLockerState.Off), Vol('D', BitLockerState.On));

        var attention = Assert.Single(items, i => i.Status == PrepareStatus.Attention);
        Assert.Contains("aka.ms/myrecoverykey", attention.Action);
        Assert.Contains("manage-bde -protectors -get D:", attention.Action);
        var notChecked = Assert.Single(items, i => i.Status == PrepareStatus.NotChecked);
        Assert.Contains("manage-bde -protectors -get D:", notChecked.Action);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task Encrypted_other_volume_elevated_without_recovery_password_says_to_add_one()
    {
        var items = await RunAsync(true, Vol('C', BitLockerState.Off), Vol('D', BitLockerState.On, recovery: false));

        var attention = Assert.Single(items);
        Assert.Equal(PrepareStatus.Attention, attention.Status);
        Assert.Contains("manage-bde -protectors -add D: -RecoveryPassword", attention.Action);
    }

    [Fact]
    public async Task Encrypted_other_volume_elevated_with_recovery_password_gives_only_attention()
    {
        var items = await RunAsync(true, Vol('C', BitLockerState.Off), Vol('D', BitLockerState.On, recovery: true));

        var attention = Assert.Single(items);
        Assert.Equal(PrepareStatus.Attention, attention.Status);
        Assert.DoesNotContain("-add", attention.Action);
    }

    [Fact]
    public async Task Other_volume_off_has_no_item()
    {
        Assert.Empty(await RunAsync(false, Vol('C', BitLockerState.Off), Vol('D', BitLockerState.Off)));
    }

    [Fact]
    public async Task Other_volume_unknown_is_not_checked()
    {
        var items = await RunAsync(false, Vol('C', BitLockerState.Off), Vol('D', BitLockerState.Unknown));

        var item = Assert.Single(items);
        Assert.Equal(PrepareStatus.NotChecked, item.Status);
        Assert.Contains("D:", item.Title);
    }
}
