using FormatKit.Core.Profiles;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Prepare.Checks.Apps;
using static FormatKit.Prepare.Tests.Checks.Apps.AppsTestHelpers;

namespace FormatKit.Prepare.Tests.Checks.Apps;

public class ProfileInputsCheckTests : InvariantCultureTests
{
    private static Task<IReadOnlyList<PrepareItem>> RunAsync(
        InMemoryFileSystem files, LoadedProfile? profile, params ExternalInput[] inputs) =>
        new ProfileInputsCheck().RunAsync(
            PrepareTestContext.Create(TestServices.Create(files: files), profile: profile, inputs: inputs),
            CancellationToken.None);

    private static LoadedProfile AnyProfile() => Profile(("programs", """{"enabled":true}"""));

    [Fact]
    public void Identity()
    {
        var check = new ProfileInputsCheck();
        Assert.Equal("apps.profile-inputs", check.Id);
        Assert.Equal(PrepareGroup.Apps, check.Group);
    }

    [Fact]
    public async Task Without_a_profile_there_is_no_item() =>
        Assert.Empty(await RunAsync(new InMemoryFileSystem(), null,
            new ExternalInput("modules.programs.direct[0].path", @"C:\a.exe", ExternalInputKind.File)));

    [Fact]
    public async Task Direct_installer_on_the_system_drive_is_a_blocker_naming_the_json_path()
    {
        var files = new InMemoryFileSystem();
        files.Seed(@"C:\setup.exe", "x");

        var item = Assert.Single(await RunAsync(files, AnyProfile(),
            new ExternalInput("modules.programs.direct[1].path", @"C:\setup.exe", ExternalInputKind.File)));

        Assert.Equal(PrepareStatus.Blocker, item.Status);
        Assert.Equal("apps.profile-inputs", item.CheckId);
        Assert.Contains("modules.programs.direct[1].path", item.Title);
    }

    [Fact]
    public async Task Missing_dotfiles_folder_is_a_blocker()
    {
        var item = Assert.Single(await RunAsync(new InMemoryFileSystem(), AnyProfile(),
            new ExternalInput("modules.dev.dotfiles.path", @"E:\dotfiles", ExternalInputKind.Folder)));

        Assert.Equal(PrepareStatus.Blocker, item.Status);
        Assert.Contains("modules.dev.dotfiles.path", item.Title);
    }

    [Fact]
    public async Task Profile_file_on_the_system_drive_is_a_blocker_named_the_profile_file()
    {
        var files = new InMemoryFileSystem();
        files.Seed(@"C:\profiles\p.json", "{}");

        var item = Assert.Single(await RunAsync(files, AnyProfile(),
            new ExternalInput("", @"C:\profiles\p.json", ExternalInputKind.File)));

        Assert.Equal(PrepareStatus.Blocker, item.Status);
        Assert.Contains("profile file", item.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inputs_that_are_all_fine_give_one_ok_line_with_the_count()
    {
        var files = new InMemoryFileSystem();
        files.Seed(@"E:\p.json", "{}");
        files.Seed(@"E:\a.exe", "x");
        files.SeedDirectory(@"E:\dot");

        var item = Assert.Single(await RunAsync(files, AnyProfile(),
            new ExternalInput("", @"E:\p.json", ExternalInputKind.File),
            new ExternalInput("modules.programs.direct[0].path", @"E:\a.exe", ExternalInputKind.File),
            new ExternalInput("modules.dev.dotfiles.path", @"E:\dot", ExternalInputKind.Folder)));

        Assert.Equal(PrepareStatus.Ok, item.Status);
        Assert.Contains("3 profile files are off C:", item.Title);
    }

    [Fact]
    public async Task Import_file_and_network_backup_inputs_are_left_to_the_other_checks() =>
        Assert.Empty(await RunAsync(new InMemoryFileSystem(), AnyProfile(),
            new ExternalInput("modules.programs.importFile", @"C:\p.json", ExternalInputKind.File),
            new ExternalInput("modules.drivers.networkDriverBackup", @"C:\net", ExternalInputKind.Folder)));

    [Fact]
    public async Task An_input_off_the_system_drive_is_not_checked_when_the_volume_list_is_unknown()
    {
        var files = new InMemoryFileSystem();
        files.SeedDirectory(@"E:\dotfiles");
        var context = PrepareTestContext.Create(TestServices.Create(files: files), profile: AnyProfile(),
            inputs: [new ExternalInput("modules.dev.dotfiles.path", @"E:\dotfiles", ExternalInputKind.Folder)],
            windows: PrepareTestContext.UnknownVolumes());

        var item = Assert.Single(await new ProfileInputsCheck().RunAsync(context, CancellationToken.None));

        Assert.Equal(PrepareStatus.NotChecked, item.Status);
        Assert.Contains("modules.dev.dotfiles.path", item.Title);
        Assert.Contains(@"E:\dotfiles", item.Detail);
    }
}
