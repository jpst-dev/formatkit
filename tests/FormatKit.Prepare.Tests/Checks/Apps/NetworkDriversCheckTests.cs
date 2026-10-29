using FormatKit.Core.Profiles;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Prepare.Checks.Apps;
using static FormatKit.Prepare.Tests.Checks.Apps.AppsTestHelpers;

namespace FormatKit.Prepare.Tests.Checks.Apps;

public class NetworkDriversCheckTests : InvariantCultureTests
{
    private const string Path = "modules.drivers.networkDriverBackup";

    private static LoadedProfile Enabled() => Profile(("drivers", """{"enabled":true}"""));

    private static Task<IReadOnlyList<PrepareItem>> RunAsync(
        InMemoryFileSystem files, LoadedProfile? profile = null, IReadOnlyList<VolumeInfo>? volumes = null,
        params ExternalInput[] inputs) =>
        new NetworkDriversCheck().RunAsync(
            PrepareTestContext.Create(TestServices.Create(files: files), volumes, profile: profile, inputs: inputs),
            CancellationToken.None);

    [Fact]
    public void Identity()
    {
        var check = new NetworkDriversCheck();
        Assert.Equal("apps.network-drivers", check.Id);
        Assert.Equal(PrepareGroup.Apps, check.Group);
    }

    [Fact]
    public async Task Without_a_profile_it_asks_for_a_backup()
    {
        var item = Assert.Single(await RunAsync(new InMemoryFileSystem()));

        Assert.Equal(PrepareStatus.Attention, item.Status);
        Assert.Equal("apps.network-drivers", item.CheckId);
        Assert.Contains(@"formatkit drivers backup-network --output E:\netdrivers", item.Action);
    }

    [Fact]
    public async Task Folder_off_the_system_drive_with_an_inf_is_ok()
    {
        var files = new InMemoryFileSystem();
        files.Seed(@"E:\net\a\x.inf", "x");

        var item = Assert.Single(await RunAsync(files, Enabled(), null,
            new ExternalInput(Path, @"E:\net", ExternalInputKind.Folder)));

        Assert.Equal(PrepareStatus.Ok, item.Status);
        Assert.Contains(@"E:\net", item.Title);
    }

    [Fact]
    public async Task Folder_on_the_system_drive_is_a_blocker()
    {
        var files = new InMemoryFileSystem();
        files.Seed(@"C:\net\x.inf", "x");

        var item = Assert.Single(await RunAsync(files, Enabled(), null,
            new ExternalInput(Path, @"C:\net", ExternalInputKind.Folder)));

        Assert.Equal(PrepareStatus.Blocker, item.Status);
        Assert.Contains(@"C:\net", item.Title);
    }

    [Fact]
    public async Task Folder_without_an_inf_is_a_blocker_with_the_backup_command()
    {
        var files = new InMemoryFileSystem();
        files.SeedDirectory(@"E:\empty");
        files.Seed(@"E:\empty\readme.txt", "x");

        var item = Assert.Single(await RunAsync(files, Enabled(), null,
            new ExternalInput(Path, @"E:\empty", ExternalInputKind.Folder)));

        Assert.Equal(PrepareStatus.Blocker, item.Status);
        Assert.Contains("drivers backup-network", item.Action);
    }

    [Fact]
    public async Task Missing_folder_is_a_blocker()
    {
        var item = Assert.Single(await RunAsync(new InMemoryFileSystem(), Enabled(), null,
            new ExternalInput(Path, @"E:\nowhere", ExternalInputKind.Folder)));

        Assert.Equal(PrepareStatus.Blocker, item.Status);
        Assert.Contains("drivers backup-network", item.Action);
    }

    [Fact]
    public async Task Folder_on_a_lost_vhdx_volume_is_a_blocker()
    {
        var files = new InMemoryFileSystem();
        files.Seed(@"D:\net\x.inf", "x");
        VolumeInfo[] volumes =
        [
            new('C', 0, BitLockerState.Off, null, null),
            new('D', null, BitLockerState.Off, @"C:\DevDrive\dev.vhdx", null),
        ];

        var item = Assert.Single(await RunAsync(files, Enabled(), volumes,
            new ExternalInput(Path, @"D:\net", ExternalInputKind.Folder)));

        Assert.Equal(PrepareStatus.Blocker, item.Status);
    }

    [Fact]
    public async Task Unreadable_folder_is_not_checked_and_does_not_crash()
    {
        var files = new InMemoryFileSystem();
        files.SeedDirectory(@"E:\net");
        files.MarkUnreadable(@"E:\net");

        var item = Assert.Single(await RunAsync(files, Enabled(), null,
            new ExternalInput(Path, @"E:\net", ExternalInputKind.Folder)));

        Assert.Equal(PrepareStatus.NotChecked, item.Status);
    }

    [Fact]
    public async Task Enabled_section_without_the_input_asks_for_a_backup()
    {
        var item = Assert.Single(await RunAsync(new InMemoryFileSystem(), Enabled()));

        Assert.Equal(PrepareStatus.Attention, item.Status);
        Assert.Contains("formatkit drivers backup-network", item.Action);
    }

    [Theory]
    [InlineData("""{"enabled":false}""")]
    [InlineData("""{}""")]
    public async Task Disabled_drivers_section_gives_no_item(string drivers) =>
        Assert.Empty(await RunAsync(new InMemoryFileSystem(), Profile(("drivers", drivers))));

    [Fact]
    public async Task Profile_without_drivers_section_gives_no_item() =>
        Assert.Empty(await RunAsync(new InMemoryFileSystem(), Profile(("dev", """{"enabled":true}"""))));

    [Fact]
    public async Task Folder_off_the_system_drive_with_an_inf_is_not_checked_when_the_volume_list_is_unknown()
    {
        var files = new InMemoryFileSystem();
        files.Seed(@"E:\net\a\x.inf", "x");
        var context = PrepareTestContext.Create(TestServices.Create(files: files), profile: Enabled(),
            inputs: [new ExternalInput(Path, @"E:\net", ExternalInputKind.Folder)],
            windows: PrepareTestContext.UnknownVolumes());

        var item = Assert.Single(await new NetworkDriversCheck().RunAsync(context, CancellationToken.None));

        Assert.Equal(PrepareStatus.NotChecked, item.Status);
        Assert.Contains(@"E:\net", item.Detail);
    }
}
