using System.Globalization;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Prepare.Checks.Data;

namespace FormatKit.Prepare.Tests.Checks.Data;

public class UserFoldersCheckTests : IDisposable
{
    private const string ShellKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";
    private const long MB = 1024 * 1024;
    private const FileAttributes Recall = (FileAttributes)0x400000;

    private readonly CultureInfo _saved = CultureInfo.CurrentUICulture;
    private readonly InMemoryFileSystem _files = new();
    private readonly FakeRegistry _registry = new();
    private readonly FakeUserEnvironment _userEnv = new();

    public UserFoldersCheckTests() => CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

    public void Dispose() => CultureInfo.CurrentUICulture = _saved;

    private async Task<IReadOnlyList<PrepareItem>> RunAsync(WindowsVolume? windows = null)
    {
        var context = PrepareTestContext.Create(
            TestServices.Create(files: _files, registry: _registry, userEnvironment: _userEnv), windows: windows);
        return await new UserFoldersCheck().RunAsync(context, CancellationToken.None);
    }

    private void Shell(string name, string value) => _registry.Set(ShellKey, name, RegistryKind.ExpandString, value);

    [Fact]
    public void Identity()
    {
        var check = new UserFoldersCheck();
        Assert.Equal("data.user-folders", check.Id);
        Assert.Equal(PrepareGroup.Data, check.Group);
    }

    [Fact]
    public async Task Documents_with_files_is_attention_with_size_and_copy_action()
    {
        Shell("Personal", @"%USERPROFILE%\Documents");
        _files.SeedEntry(@"C:\Users\joao\Documents\a.docx", MB);
        _files.SeedEntry(@"C:\Users\joao\Documents\sub\b.docx", MB);

        var item = Assert.Single(await RunAsync(), i => i.Status == PrepareStatus.Attention);

        Assert.Equal("data.user-folders", item.CheckId);
        Assert.Contains("Documents", item.Title);
        Assert.Contains("2.0 MB", item.Detail);
        Assert.Contains("external drive", item.Action);
    }

    [Fact]
    public async Task Folder_under_onedrive_is_ok_synced()
    {
        _userEnv.Set("OneDrive", @"C:\Users\joao\OneDrive");
        Shell("My Pictures", @"%USERPROFILE%\OneDrive\Pictures");
        _files.SeedEntry(@"C:\Users\joao\OneDrive\Pictures\p.jpg", 5 * MB);

        var items = await RunAsync();

        var item = Assert.Single(items, i => i.Title.Contains("Pictures"));
        Assert.Equal(PrepareStatus.Ok, item.Status);
        Assert.Contains("OneDrive", item.Detail);
    }

    [Fact]
    public async Task Cloud_only_files_do_not_count()
    {
        Shell("Personal", @"%USERPROFILE%\Documents");
        _files.SeedEntry(@"C:\Users\joao\Documents\big.iso", 5L * 1024 * MB, Recall);
        _files.SeedEntry(@"C:\Users\joao\Documents\offline.bin", 5L * 1024 * MB, FileAttributes.Offline);
        _files.SeedEntry(@"C:\Users\joao\Documents\small.txt", 1024);

        var item = Assert.Single(await RunAsync(), i => i.Status == PrepareStatus.Attention);

        Assert.Contains("1.0 KB", item.Detail);
    }

    [Fact]
    public async Task Redirected_folder_off_the_system_drive_is_ok()
    {
        Shell("Desktop", @"D:\Desktop");
        _files.SeedEntry(@"D:\Desktop\x.txt", MB);

        var item = Assert.Single(await RunAsync(), i => i.Title.Contains("Desktop"));

        Assert.Equal(PrepareStatus.Ok, item.Status);
    }

    [Fact]
    public async Task Empty_folder_is_ok()
    {
        Shell("My Music", @"%USERPROFILE%\Music");
        _files.SeedDirectory(@"C:\Users\joao\Music");

        var item = Assert.Single(await RunAsync(), i => i.Title.Contains("Music"));

        Assert.Equal(PrepareStatus.Ok, item.Status);
    }

    [Fact]
    public async Task Missing_registry_value_falls_back_to_the_default_folder()
    {
        _files.SeedEntry(@"C:\Users\joao\Videos\v.mp4", MB);
        _files.SeedEntry(@"C:\Users\joao\Downloads\d.zip", MB);

        var items = await RunAsync();

        Assert.Equal(PrepareStatus.Attention, Assert.Single(items, i => i.Title.Contains("Videos")).Status);
        Assert.Equal(PrepareStatus.Attention, Assert.Single(items, i => i.Title.Contains("Downloads")).Status);
        Assert.Equal(6, items.Count);
    }

    [Fact]
    public async Task Unreadable_folder_is_counted_in_the_detail()
    {
        Shell("Personal", @"%USERPROFILE%\Documents");
        _files.SeedEntry(@"C:\Users\joao\Documents\a.docx", MB);
        _files.SeedDirectory(@"C:\Users\joao\Documents\locked");
        _files.MarkUnreadable(@"C:\Users\joao\Documents\locked");

        var item = Assert.Single(await RunAsync(), i => i.Status == PrepareStatus.Attention);

        Assert.Contains("1 folder could not be read", item.Detail);
    }

    [Fact]
    public async Task Several_unreadable_folders_use_the_plural()
    {
        Shell("Personal", @"%USERPROFILE%\Documents");
        _files.SeedEntry(@"C:\Users\joao\Documents\a.docx", MB);
        foreach (var name in new[] { "l1", "l2" })
        {
            _files.SeedDirectory(@"C:\Users\joao\Documents\" + name);
            _files.MarkUnreadable(@"C:\Users\joao\Documents\" + name);
        }

        var item = Assert.Single(await RunAsync(), i => i.Status == PrepareStatus.Attention);

        Assert.Contains("2 folders could not be read", item.Detail);
    }

    [Fact]
    public async Task Single_file_uses_the_singular()
    {
        Shell("Personal", @"%USERPROFILE%\Documents");
        _files.SeedEntry(@"C:\Users\joao\Documents\a.docx", MB);

        var item = Assert.Single(await RunAsync(), i => i.Status == PrepareStatus.Attention);

        Assert.Contains("1.0 MB in 1 file in", item.Detail);
    }

    [Fact]
    public async Task Empty_folder_with_unreadable_subfolders_is_not_checked_not_ok()
    {
        Shell("My Music", @"%USERPROFILE%\Music");
        _files.SeedDirectory(@"C:\Users\joao\Music\locked");
        _files.MarkUnreadable(@"C:\Users\joao\Music\locked");

        var item = Assert.Single(await RunAsync(), i => i.Title.Contains("Music"));

        Assert.Equal(PrepareStatus.NotChecked, item.Status);
        Assert.Contains("1 folder could not be read", item.Detail);
        Assert.NotNull(item.Action);
    }

    [Fact]
    public async Task A_folder_off_the_system_drive_is_not_checked_when_the_volume_list_is_unknown()
    {
        // D: could be a Dev Drive stored on C:, so "not on the Windows drive" cannot be claimed.
        Shell("Personal", @"D:\Docs");

        var item = Assert.Single(await RunAsync(PrepareTestContext.UnknownVolumes()), i => i.Title.Contains("Documents"));

        Assert.Equal(PrepareStatus.NotChecked, item.Status);
        Assert.Contains(@"D:\Docs", item.Detail);
        Assert.Contains("could not be read", item.Detail);
        Assert.Contains("Disk Management", item.Action);
    }
}
