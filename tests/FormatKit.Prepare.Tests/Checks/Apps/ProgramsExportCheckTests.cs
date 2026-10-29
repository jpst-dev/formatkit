using FormatKit.Core.Profiles;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Prepare.Checks.Apps;
using static FormatKit.Prepare.Tests.Checks.Apps.AppsTestHelpers;

namespace FormatKit.Prepare.Tests.Checks.Apps;

public class ProgramsExportCheckTests : InvariantCultureTests
{
    private const string Path = "modules.programs.importFile";

    private static Task<IReadOnlyList<PrepareItem>> RunAsync(
        InMemoryFileSystem files, LoadedProfile? profile = null, params ExternalInput[] inputs) =>
        new ProgramsExportCheck().RunAsync(
            PrepareTestContext.Create(TestServices.Create(files: files), profile: profile, inputs: inputs),
            CancellationToken.None);

    [Fact]
    public void Identity()
    {
        var check = new ProgramsExportCheck();
        Assert.Equal("apps.programs-export", check.Id);
        Assert.Equal(PrepareGroup.Apps, check.Group);
    }

    [Fact]
    public async Task Without_a_profile_it_asks_for_an_export()
    {
        var item = Assert.Single(await RunAsync(new InMemoryFileSystem()));

        Assert.Equal(PrepareStatus.Attention, item.Status);
        Assert.Equal("apps.programs-export", item.CheckId);
        Assert.Contains(@"formatkit programs export E:\programs.json", item.Action);
    }

    [Fact]
    public async Task Import_file_off_the_system_drive_is_ok()
    {
        var files = new InMemoryFileSystem();
        files.Seed(@"E:\p.json", "[]");

        var item = Assert.Single(await RunAsync(files, Profile(("programs", """{"enabled":true}""")),
            new ExternalInput(Path, @"E:\p.json", ExternalInputKind.File)));

        Assert.Equal(PrepareStatus.Ok, item.Status);
        Assert.Contains(@"E:\p.json", item.Title);
    }

    [Fact]
    public async Task Import_file_on_the_system_drive_is_a_blocker()
    {
        var files = new InMemoryFileSystem();
        files.Seed(@"C:\p.json", "[]");

        var item = Assert.Single(await RunAsync(files, Profile(("programs", """{"enabled":true}""")),
            new ExternalInput(Path, @"C:\p.json", ExternalInputKind.File)));

        Assert.Equal(PrepareStatus.Blocker, item.Status);
        Assert.Contains(@"C:\p.json", item.Title);
        Assert.Contains("move it off C:", item.Action, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_import_file_is_a_blocker()
    {
        var item = Assert.Single(await RunAsync(new InMemoryFileSystem(), Profile(("programs", """{"enabled":true}""")),
            new ExternalInput(Path, @"E:\gone.json", ExternalInputKind.File)));

        Assert.Equal(PrepareStatus.Blocker, item.Status);
        Assert.Contains(@"E:\gone.json", item.Title);
    }

    [Fact]
    public async Task Enabled_section_without_import_file_asks_for_an_export()
    {
        var item = Assert.Single(await RunAsync(new InMemoryFileSystem(), Profile(("programs", """{"enabled":true}"""))));

        Assert.Equal(PrepareStatus.Attention, item.Status);
        Assert.Contains(@"formatkit programs export E:\programs.json", item.Action);
    }

    [Theory]
    [InlineData("""{"enabled":false}""")]
    [InlineData("""{}""")]
    public async Task Disabled_programs_section_gives_no_item(string programs) =>
        Assert.Empty(await RunAsync(new InMemoryFileSystem(), Profile(("programs", programs))));

    [Fact]
    public async Task Profile_without_programs_section_gives_no_item() =>
        Assert.Empty(await RunAsync(new InMemoryFileSystem(), Profile(("dev", """{"enabled":true}"""))));
}
