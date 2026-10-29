using System.Globalization;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Prepare.Checks.Data;

namespace FormatKit.Prepare.Tests.Checks.Data;

public class OtherUsersCheckTests : IDisposable
{
    private const string ProfileList = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    private const string Current = "S-1-5-21-1-2-3-1001";
    private const string Maria = "S-1-5-21-1-2-3-1002";

    private readonly CultureInfo _saved = CultureInfo.CurrentUICulture;
    private readonly InMemoryFileSystem _files = new();
    private readonly FakeRegistry _registry = new();

    public OtherUsersCheckTests() => CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

    public void Dispose() => CultureInfo.CurrentUICulture = _saved;

    private void Profile(string sid, string path) =>
        _registry.Set($@"{ProfileList}\{sid}", "ProfileImagePath", RegistryKind.ExpandString, path);

    private async Task<IReadOnlyList<PrepareItem>> RunAsync()
    {
        var context = PrepareTestContext.Create(TestServices.Create(
            files: _files, registry: _registry, session: new FakeUserSession { CurrentUserSid = Current }));
        return await new OtherUsersCheck().RunAsync(context, CancellationToken.None);
    }

    [Fact]
    public void Identity()
    {
        var check = new OtherUsersCheck();
        Assert.Equal("data.other-users", check.Id);
        Assert.Equal(PrepareGroup.Data, check.Group);
    }

    [Fact]
    public async Task Other_local_profile_is_attention_with_name_and_size()
    {
        Profile(Current, @"C:\Users\joao");
        Profile("S-1-5-18", @"%systemroot%\system32\config\systemprofile");
        Profile(Maria, @"C:\Users\maria");
        _files.SeedEntry(@"C:\Users\maria\Documents\a.docx", 3 * 1024 * 1024);

        var item = Assert.Single(await RunAsync());

        Assert.Equal(PrepareStatus.Attention, item.Status);
        Assert.Equal("data.other-users", item.CheckId);
        Assert.Contains("maria", item.Title);
        Assert.Contains("3.0 MB", item.Detail);
    }

    [Fact]
    public async Task Size_is_omitted_when_a_folder_was_unreadable()
    {
        Profile(Maria, @"C:\Users\maria");
        _files.SeedEntry(@"C:\Users\maria\a.txt", 3 * 1024 * 1024);
        _files.SeedDirectory(@"C:\Users\maria\locked");
        _files.MarkUnreadable(@"C:\Users\maria\locked");

        var item = Assert.Single(await RunAsync());

        Assert.Contains("maria", item.Title);
        Assert.DoesNotContain("MB", item.Detail);
    }

    [Fact]
    public async Task Profile_whose_folder_is_missing_gives_no_item()
    {
        Profile(Maria, @"C:\Users\maria");

        Assert.Empty(await RunAsync());
    }

    [Fact]
    public async Task Well_known_system_sids_are_skipped()
    {
        foreach (var sid in new[] { "S-1-5-18", "S-1-5-19", "S-1-5-20" })
        {
            Profile(sid, @"C:\Windows\ServiceProfiles\" + sid);
            _files.SeedEntry($@"C:\Windows\ServiceProfiles\{sid}\x.dat", 10);
        }

        Assert.Empty(await RunAsync());
    }
}
