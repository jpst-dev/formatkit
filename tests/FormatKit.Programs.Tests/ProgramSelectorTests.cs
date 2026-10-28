using FormatKit.Programs.Catalog;
using static FormatKit.Programs.Tests.ProgramsTestData;

namespace FormatKit.Programs.Tests;

public class ProgramSelectorTests
{
    private static ProgramsOptions Options(string[] catalog, WingetEntry[] winget) =>
        new(true, false, catalog, winget, null, []);

    [Fact]
    public void Duplicates_across_sources_install_once_first_wins()
    {
        var options = Options(
            ["browsers.firefox"],
            [new("mozilla.firefox", "winget", "1.0", InstallContext.User), new("Git.Git", "winget", null, InstallContext.Machine)]);
        var imported = new WingetEntry[]
        {
            new("GIT.GIT", "winget", null, InstallContext.Machine),
            new("Git.Git", "msstore", null, InstallContext.Machine),
        };

        var selected = ProgramSelector.Select(options, TestCatalog(), imported);

        Assert.Equal(["browsers.firefox", "winget:winget:Git.Git", "winget:msstore:Git.Git"], selected.Select(s => s.Key));
        Assert.Null(selected[0].Package.Version);
        Assert.Equal(InstallContext.Machine, selected[0].Package.Context);
    }

    [Fact]
    public void Non_catalog_programs_have_no_category()
    {
        var options = Options(["communication.discord"], [new("Git.Git", "winget", null, InstallContext.Machine)]);

        var selected = ProgramSelector.Select(options, TestCatalog(), [new("9NKSQGP7F2NH", "msstore", null, InstallContext.Machine)]);

        Assert.Equal(ProgramCategory.Communication, selected[0].Category);
        Assert.Equal(InstallContext.User, selected[0].Package.Context);
        Assert.Equal("Discord", selected[0].DisplayName);
        Assert.Null(selected[1].Category);
        Assert.Null(selected[2].Category);
        Assert.Equal("winget:msstore:9NKSQGP7F2NH", selected[2].Key);
    }
}
