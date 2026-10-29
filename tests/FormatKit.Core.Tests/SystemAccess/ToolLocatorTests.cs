using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class ToolLocatorTests
{
    private static readonly string Fresh = Path.Combine(@"C:\Fresh", "git.exe");
    private static readonly string Known = Path.Combine(@"C:\Known", "git.exe");

    [Fact]
    public void ToolLocator_prefers_fresh_path_over_known_paths()
    {
        var files = new InMemoryFileSystem();
        files.Seed(Fresh, "x");
        files.Seed(Known, "x");
        var locator = new ToolLocator(files, _ => @"C:\Other;C:\Fresh", _ => null);

        Assert.Equal(Fresh, locator.Find("git.exe", [Known]));
    }

    [Fact]
    public void ToolLocator_checks_user_path_after_machine_path()
    {
        var files = new InMemoryFileSystem();
        files.Seed(Fresh, "x");
        var locator = new ToolLocator(files, _ => @"C:\Other", _ => @"C:\Fresh");

        Assert.Equal(Fresh, locator.Find("git.exe", []));
    }

    [Fact]
    public void ToolLocator_falls_back_to_known_path()
    {
        var files = new InMemoryFileSystem();
        files.Seed(Known, "x");
        var locator = new ToolLocator(files, _ => @"C:\Other", _ => null);

        Assert.Equal(Known, locator.Find("git.exe", [@"C:\Missing\git.exe", Known]));
    }

    [Fact]
    public void ToolLocator_returns_null_when_missing()
    {
        var locator = new ToolLocator(new InMemoryFileSystem(), _ => @"C:\Other", _ => null);

        Assert.Null(locator.Find("git.exe", [Known]));
    }
}
