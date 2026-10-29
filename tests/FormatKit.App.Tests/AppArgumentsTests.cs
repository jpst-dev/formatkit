namespace FormatKit.App.Tests;

public class AppArgumentsTests
{
    [Fact]
    public void Parse_NoArgs_Window()
    {
        var args = AppArguments.Parse([]);

        Assert.Equal(AppMode.Window, args.Mode);
        Assert.Null(args.PipeName);
        Assert.Empty(args.Job);
    }

    [Fact]
    public void Parse_Resume()
    {
        var args = AppArguments.Parse(["--resume"]);

        Assert.Equal(AppMode.Resume, args.Mode);
    }

    [Fact]
    public void Parse_Worker_SplitsPipeAndJob()
    {
        var args = AppArguments.Parse(["--worker", "--pipe", "FormatKit-x", "run", "--profile", "C:\\a b\\p.json"]);

        Assert.Equal(AppMode.Worker, args.Mode);
        Assert.Equal("FormatKit-x", args.PipeName);
        Assert.Equal(["run", "--profile", "C:\\a b\\p.json"], args.Job);
    }

    [Theory]
    [InlineData("--bogus")]
    [InlineData("--worker")]
    public void Parse_Unknown_Window(string arg)
    {
        Assert.Equal(AppMode.Window, AppArguments.Parse([arg]).Mode);
    }
}
