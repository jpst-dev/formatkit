using FormatKit.Core.Profiles;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Hosting;

namespace FormatKit.Hosting.Tests;

public sealed class ProfileServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fk-hosting-").FullName;
    private readonly FakeModule _module = new("drivers");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ProfileService Service() => new([_module]);

    private string Write(string json)
    {
        var path = Path.Combine(_dir, "profile.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Load_ValidFile_ReturnsProfileAndNoErrors()
    {
        var path = Write("""{"schemaVersion":1,"name":"Mine","modules":{"drivers":{"enabled":true}}}""");

        var outcome = Service().Load(path);

        Assert.Empty(outcome.Errors);
        Assert.NotNull(outcome.Profile);
        Assert.Equal("Mine", outcome.Profile.Profile.Name);
        Assert.Equal(path, outcome.Profile.Path);
    }

    [Fact]
    public void Load_InvalidFile_ReturnsErrorsWithPaths()
    {
        var path = Write("""{"schemaVersion":1,"name":"Mine","modules":{"nope":{"enabled":true}}}""");

        var outcome = Service().Load(path);

        Assert.Null(outcome.Profile);
        var error = Assert.Single(outcome.Errors);
        Assert.False(string.IsNullOrEmpty(error.Path));
    }

    [Fact]
    public void Load_MissingFile_ReturnsOneError()
    {
        var outcome = Service().Load(Path.Combine(_dir, "missing.json"));

        Assert.Null(outcome.Profile);
        Assert.Single(outcome.Errors);
    }

    [Fact]
    public void Load_DriversOnlyPath_ReturnsBuiltInProfile()
    {
        var outcome = Service().Load(BuiltInProfiles.DriversOnlyPath);

        Assert.Empty(outcome.Errors);
        Assert.Equal(BuiltInProfiles.DriversOnlyHash, outcome.Profile!.Hash);
        Assert.Same(BuiltInProfiles.DriversOnly, outcome.Profile.Profile);
    }
}
