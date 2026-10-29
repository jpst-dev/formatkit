using FormatKit.Core;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Programs.Steps;
using static FormatKit.Programs.Tests.ProgramsTestData;

namespace FormatKit.Programs.Tests;

public class ProgramsModuleTests
{
    private static ProgramsModule Module(InMemoryFileSystem? files = null) =>
        new(TestCatalog(), new AppPaths(@"C:\FormatKitTest"), files ?? new InMemoryFileSystem());

    [Fact]
    public void Steps_follow_category_order_then_other_then_direct()
    {
        var json = """
            { "enabled": true, "acceptAgreements": true,
              "catalog": ["games.steam", "communication.discord", "browsers.firefox"],
              "winget": [ { "id": "Notepad++.Notepad++" } ],
              "direct": [ { "name": "Tool", "path": "D:\\s.exe" } ] }
            """;

        var steps = Module().CreateSteps(Json(json));

        Assert.Equal(
            ["programs.browsers", "programs.communication", "programs.games", "programs.other", "programs.direct"],
            steps.Select(s => s.Id));
        Assert.All(steps, s => Assert.False(string.IsNullOrWhiteSpace(s.Title)));
        Assert.Equal("Firefox", Assert.IsType<WingetProgramsStep>(steps[0]).Programs.Single().DisplayName);
    }

    [Fact]
    public void Empty_section_creates_no_steps()
    {
        Assert.Empty(Module().CreateSteps(Json("""{ "enabled": true, "acceptAgreements": true }""")));
        Assert.Empty(Module().CreateSteps(Json("""{ "enabled": false, "catalog": ["browsers.firefox"], "acceptAgreements": true }""")));
    }

    [Fact]
    public void Relative_paths_resolve_against_the_base_directory()
    {
        var files = new InMemoryFileSystem();
        files.Seed(@"E:\usb\old-pc.json", ExportSample);
        var json = """
            { "enabled": true, "acceptAgreements": true, "importFile": "old-pc.json",
              "direct": [ { "name": "Tool", "path": "setups\\tool.exe" }, { "name": "Abs", "path": "D:\\abs.exe" } ] }
            """;

        Assert.Empty(Module(files).Validate(Json(json), "modules.programs", @"E:\usb"));
        var steps = Module(files).CreateSteps(Json(json), @"E:\usb");

        var direct = Assert.IsType<DirectProgramsStep>(steps[^1]);
        Assert.Equal([@"E:\usb\setups\tool.exe", @"D:\abs.exe"], direct.Entries.Select(e => e.Path));
        Assert.Contains(steps.OfType<WingetProgramsStep>().SelectMany(s => s.Programs), p => p.Package.Id == "Git.Git");
    }

    [Fact]
    public void Validate_returns_option_errors()
    {
        var errors = Module().Validate(Json("""{ "enabled": true, "catalog": ["nope.nope"], "acceptAgreements": true }"""), "$.modules.programs");

        Assert.Contains(errors, e => e.Path == "$.modules.programs.catalog[0]");
    }
}
