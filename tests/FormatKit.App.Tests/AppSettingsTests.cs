using FormatKit.App.Services;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.App.Tests;

public class AppSettingsTests
{
    private const string Path = @"C:\Users\x\AppData\Local\FormatKit\app.json";

    [Fact]
    public void LastProfile_IsPersistedAndReadBack()
    {
        var files = new InMemoryFileSystem();

        new AppSettings(files, Path).LastProfile = @"C:\p\mine.json";

        Assert.Equal(@"C:\p\mine.json", new AppSettings(files, Path).LastProfile);
        Assert.Contains("\"lastProfile\"", files.Files[Path]);
    }

    [Fact]
    public void Settings_MissingFile_ReadsEmpty()
    {
        Assert.Null(new AppSettings(new InMemoryFileSystem(), Path).LastProfile);
    }

    [Theory]
    [InlineData("not json {")]
    [InlineData("[1,2]")]
    [InlineData("{\"lastProfile\": 5}")]
    public void Settings_CorruptFile_ReadsEmpty(string contents)
    {
        var files = new InMemoryFileSystem();
        files.Seed(Path, contents);

        Assert.Null(new AppSettings(files, Path).LastProfile);
    }

    [Fact]
    public void LastProfile_WriteFailure_IsIgnored()
    {
        var files = new InMemoryFileSystem { AtomicWriteError = new IOException("disk") };

        var settings = new AppSettings(files, Path) { LastProfile = @"C:\p\a.json" };

        Assert.Equal(@"C:\p\a.json", settings.LastProfile);
    }
}
