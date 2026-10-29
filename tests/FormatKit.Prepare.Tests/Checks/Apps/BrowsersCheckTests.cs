using FormatKit.Core.Tests.Fakes;
using FormatKit.Prepare.Checks.Apps;

namespace FormatKit.Prepare.Tests.Checks.Apps;

public class BrowsersCheckTests : InvariantCultureTests
{
    private static Task<IReadOnlyList<PrepareItem>> RunAsync(InMemoryFileSystem files) =>
        new BrowsersCheck().RunAsync(
            PrepareTestContext.Create(TestServices.Create(files: files)), CancellationToken.None);

    [Fact]
    public void Identity()
    {
        var check = new BrowsersCheck();
        Assert.Equal("apps.browsers", check.Id);
        Assert.Equal(PrepareGroup.Apps, check.Group);
    }

    [Fact]
    public async Task Chrome_and_firefox_each_give_an_attention_item()
    {
        var files = new InMemoryFileSystem();
        files.SeedDirectory(PrepareTestContext.LocalAppData + @"\Google\Chrome\User Data");
        files.SeedDirectory(PrepareTestContext.AppData + @"\Mozilla\Firefox\Profiles\abc.default");

        var items = await RunAsync(files);

        Assert.Equal(2, items.Count);
        Assert.All(items, i =>
        {
            Assert.Equal(PrepareStatus.Attention, i.Status);
            Assert.Equal("apps.browsers", i.CheckId);
            Assert.Contains("sync", i.Action, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Contains(items, i => i.Title.Contains("Chrome"));
        Assert.Contains(items, i => i.Title.Contains("Firefox"));
    }

    [Fact]
    public async Task Firefox_without_a_profile_gives_no_item()
    {
        var files = new InMemoryFileSystem();
        files.SeedDirectory(PrepareTestContext.AppData + @"\Mozilla\Firefox\Profiles");

        Assert.Empty(await RunAsync(files));
    }

    [Theory]
    [InlineData("Edge", false, @"Microsoft\Edge\User Data")]
    [InlineData("Chrome", false, @"Google\Chrome\User Data")]
    [InlineData("Brave", false, @"BraveSoftware\Brave-Browser\User Data")]
    [InlineData("Vivaldi", false, @"Vivaldi\User Data")]
    [InlineData("Opera", true, @"Opera Software\Opera Stable")]
    public async Task Each_browser_is_found_at_its_user_data_folder(string name, bool roaming, string relative)
    {
        var files = new InMemoryFileSystem();
        files.SeedDirectory((roaming ? PrepareTestContext.AppData : PrepareTestContext.LocalAppData) + @"\" + relative);

        var item = Assert.Single(await RunAsync(files));

        Assert.Contains(name, item.Title);
    }

    [Fact]
    public async Task No_browsers_means_no_items() =>
        Assert.Empty(await RunAsync(new InMemoryFileSystem()));
}
