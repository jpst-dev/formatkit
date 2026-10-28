using FormatKit.Core;
using FormatKit.Core.Profiles;
using FormatKit.Programs.Steps;
using static FormatKit.Programs.Tests.ProgramsTestData;

namespace FormatKit.Programs.Tests;

/// <summary>
/// A resumed run starts in another directory (the resume task runs from System32), so relative paths in the programs
/// section must resolve against the profile file, through the real loader and file system.
/// </summary>
public class ProfileRelativePathsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "formatkit-relpaths-" + Guid.NewGuid().ToString("N"));

    public ProfileRelativePathsTests() => Directory.CreateDirectory(Path.Combine(_dir, "setups"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Relative_import_file_and_direct_path_resolve_against_the_profile_directory()
    {
        File.WriteAllText(Path.Combine(_dir, "old-pc.json"), ExportSample);
        var profilePath = Path.Combine(_dir, "profile.json");
        File.WriteAllText(profilePath, """
            { "schemaVersion": 1, "name": "Relative",
              "modules": { "programs": { "enabled": true, "acceptAgreements": true,
                "importFile": "old-pc.json",
                "direct": [ { "name": "Tool", "path": "setups\\tool.exe" } ] } } }
            """);
        Assert.NotEqual(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(_dir)),
            Path.TrimEndingDirectorySeparator(Environment.CurrentDirectory),
            StringComparer.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(Environment.CurrentDirectory, "old-pc.json")));

        IModule[] modules = [new ProgramsModule(TestCatalog(), new AppPaths(@"C:\FormatKitTest"))];
        var result = new ProfileLoader(modules).Load(profilePath);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.Path}: {e.Message}")));
        var steps = PlanBuilder.Build(result.Value!.Profile, modules);
        Assert.Contains(steps.OfType<WingetProgramsStep>().SelectMany(s => s.Programs), p => p.Package.Id == "Git.Git");
        var direct = Assert.Single(steps.OfType<DirectProgramsStep>());
        Assert.Equal(Path.Combine(_dir, "setups", "tool.exe"), Assert.Single(direct.Entries).Path, ignoreCase: true);
    }
}
