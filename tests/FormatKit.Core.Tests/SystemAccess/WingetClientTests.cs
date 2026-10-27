using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class WingetClientTests
{
    private static ProcessResult Result(int exit, bool timedOut = false) =>
        new(exit, "out", "", TimeSpan.Zero, timedOut);

    [Fact]
    public async Task Install_passes_silent_flags()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("winget.exe", Result(0));

        await new WingetClient(runner).InstallAsync("Git.Git", TestContext.Current.CancellationToken);

        var call = Assert.Single(runner.Calls);
        Assert.Equal(
            ["install", "--id", "Git.Git", "--exact", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"],
            call.Arguments);
        Assert.Equal(Timeouts.Install, call.Timeout);
    }

    [Theory]
    [InlineData(0, WingetStatus.Installed)]
    [InlineData(-1978335189, WingetStatus.AlreadyInstalled)]
    [InlineData(-1978335135, WingetStatus.AlreadyInstalled)]
    [InlineData(1, WingetStatus.Failed)]
    public async Task Install_maps_exit_codes(int exit, WingetStatus expected)
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("winget.exe", Result(exit));

        var result = await new WingetClient(runner).InstallAsync("Git.Git", TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Status);
        Assert.Equal(exit, result.ExitCode);
    }

    [Fact]
    public async Task Install_timeout_is_failed()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("winget.exe", Result(0, timedOut: true));

        var result = await new WingetClient(runner).InstallAsync("Git.Git", TestContext.Current.CancellationToken);

        Assert.Equal(WingetStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Missing_winget_maps_to_WingetMissing()
    {
        var runner = new FakeProcessRunner();
        runner.ThrowNotFound("winget.exe");

        var result = await new WingetClient(runner).InstallAsync("Git.Git", TestContext.Current.CancellationToken);

        Assert.Equal(WingetStatus.WingetMissing, result.Status);
    }

    [Fact]
    public async Task IsInstalled_true_on_exit_zero_false_otherwise()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("winget.exe", Result(0));
        runner.Enqueue("winget.exe", Result(-1978335212));
        var client = new WingetClient(runner);

        Assert.True(await client.IsInstalledAsync("Git.Git", TestContext.Current.CancellationToken));
        Assert.False(await client.IsInstalledAsync("Git.Git", TestContext.Current.CancellationToken));
        Assert.Equal(
            ["list", "--id", "Git.Git", "--exact", "--accept-source-agreements", "--disable-interactivity"],
            runner.Calls[0].Arguments);
        Assert.Equal(Timeouts.Query, runner.Calls[0].Timeout);
    }

    [Fact]
    public async Task IsInstalled_false_on_timeout()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("winget.exe", Result(0, timedOut: true));

        Assert.False(await new WingetClient(runner).IsInstalledAsync("Git.Git", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Install_appends_source_when_given()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("winget.exe", Result(0));

        await new WingetClient(runner).InstallAsync("XP8CLZL93F5Z4P", TestContext.Current.CancellationToken, "msstore");

        Assert.Equal(
            ["install", "--id", "XP8CLZL93F5Z4P", "--exact", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity", "--source", "msstore"],
            runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task IsInstalled_appends_source_when_given()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("winget.exe", Result(0));

        await new WingetClient(runner).IsInstalledAsync("XP8CLZL93F5Z4P", TestContext.Current.CancellationToken, "msstore");

        Assert.Equal(
            ["list", "--id", "XP8CLZL93F5Z4P", "--exact", "--accept-source-agreements", "--disable-interactivity", "--source", "msstore"],
            runner.Calls[0].Arguments);
    }
}
