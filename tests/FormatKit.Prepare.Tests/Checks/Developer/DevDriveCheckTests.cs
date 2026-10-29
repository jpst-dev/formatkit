using System.Globalization;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Prepare.Checks.Developer;

namespace FormatKit.Prepare.Tests.Checks.Developer;

public class DevDriveCheckTests : IDisposable
{
    private const long Gb = 1024L * 1024 * 1024;

    private readonly CultureInfo _saved = CultureInfo.CurrentUICulture;

    public DevDriveCheckTests() => CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

    public void Dispose() => CultureInfo.CurrentUICulture = _saved;

    private static VolumeInfo Vol(char letter, string? vhdx = null) =>
        new(letter, vhdx is null ? 0 : null, BitLockerState.Off, vhdx, null);

    private static Task<IReadOnlyList<PrepareItem>> RunAsync(InMemoryFileSystem files, params VolumeInfo[] volumes) =>
        new DevDriveCheck().RunAsync(
            PrepareTestContext.Create(TestServices.Create(files: files), volumes), CancellationToken.None);

    [Fact]
    public void Identity()
    {
        var check = new DevDriveCheck();
        Assert.Equal("dev.devdrive", check.Id);
        Assert.Equal(PrepareGroup.Developer, check.Group);
    }

    [Fact]
    public async Task Volume_backed_by_a_vhdx_on_the_system_drive_is_attention_with_its_size()
    {
        var files = new InMemoryFileSystem();
        files.SeedEntry(@"C:\DevDrive\dev.vhdx", 40 * Gb);

        var item = Assert.Single(await RunAsync(files, Vol('C'), Vol('D', @"C:\DevDrive\dev.vhdx")));

        Assert.Equal(PrepareStatus.Attention, item.Status);
        Assert.Equal("dev.devdrive", item.CheckId);
        Assert.Contains("D:", item.Title);
        Assert.Contains("40.0 GB", item.Detail);
        Assert.Contains(@"C:\DevDrive\dev.vhdx", item.Detail);
        Assert.Contains("copy what you need", item.Action, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_vhdx_entry_still_reports_the_volume_without_a_size()
    {
        var item = Assert.Single(await RunAsync(new InMemoryFileSystem(), Vol('C'), Vol('D', @"C:\DevDrive\dev.vhdx")));

        Assert.Contains("D:", item.Title);
        Assert.DoesNotContain("GB", item.Detail);
    }

    [Fact]
    public async Task Volumes_not_backed_by_a_lost_vhdx_give_no_item()
    {
        var files = new InMemoryFileSystem();
        files.SeedEntry(@"E:\Disks\other.vhdx", Gb);

        Assert.Empty(await RunAsync(files, Vol('C'), Vol('D'), Vol('F', @"E:\Disks\other.vhdx")));
    }

    [Fact]
    public async Task An_unknown_volume_list_is_one_not_checked_item()
    {
        var context = PrepareTestContext.Create(windows: PrepareTestContext.UnknownVolumes());

        var item = Assert.Single(await new DevDriveCheck().RunAsync(context, CancellationToken.None));

        Assert.Equal(PrepareStatus.NotChecked, item.Status);
        Assert.Equal("dev.devdrive", item.CheckId);
        Assert.Contains("could not be read", item.Detail);
        Assert.Contains("Disk Management", item.Action);
    }

    [Fact]
    public async Task A_list_without_the_system_drive_still_reports_a_lost_vhdx_and_adds_not_checked()
    {
        var windows = new WindowsVolume('C', [Vol('D', @"C:\DevDrive\dev.vhdx")]);
        var context = PrepareTestContext.Create(windows: windows);

        var items = await new DevDriveCheck().RunAsync(context, CancellationToken.None);

        Assert.Single(items, i => i.Status == PrepareStatus.Attention && i.Title.Contains("D:"));
        Assert.Single(items, i => i.Status == PrepareStatus.NotChecked);
        Assert.Equal(2, items.Count);
    }
}
