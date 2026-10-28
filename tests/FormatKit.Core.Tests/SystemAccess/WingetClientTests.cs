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

    [Theory]
    [InlineData(0, false, WingetStatus.Installed)]
    [InlineData(-1978335189, false, WingetStatus.AlreadyInstalled)]
    [InlineData(-1978335135, false, WingetStatus.AlreadyInstalled)]
    [InlineData(-1978335212, false, WingetStatus.NotFound)]
    [InlineData(-1978334967, false, WingetStatus.RebootRequired)]
    [InlineData(-1978334966, false, WingetStatus.RebootRequired)]
    [InlineData(-1978335224, false, WingetStatus.NoNetwork)]
    [InlineData(-1978335217, false, WingetStatus.NoNetwork)]
    [InlineData(-1978335163, false, WingetStatus.NoNetwork)]
    [InlineData(-1978335157, false, WingetStatus.NoNetwork)]
    [InlineData(-1978334969, false, WingetStatus.NoNetwork)]
    [InlineData(-1978335174, false, WingetStatus.BlockedByPolicy)]
    [InlineData(-1978335205, false, WingetStatus.BlockedByPolicy)]
    [InlineData(-1978334961, false, WingetStatus.BlockedByPolicy)]
    [InlineData(-1978335098, false, WingetStatus.NoNetwork)]
    [InlineData(-1978335204, false, WingetStatus.BlockedByPolicy)]
    [InlineData(12345, false, WingetStatus.Failed)]
    [InlineData(0, true, WingetStatus.Upgraded)]
    [InlineData(-1978335189, true, WingetStatus.UpToDate)]
    public async Task Install_maps_each_documented_code(int exit, bool upgrade, WingetStatus expected)
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("winget.exe", Result(exit));
        var client = new WingetClient(runner);
        var package = new WingetPackageRef("Git.Git");

        var result = upgrade
            ? await client.UpgradeAsync(package, TestContext.Current.CancellationToken)
            : await client.InstallAsync(package, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Status);
        Assert.False(result.TimedOut);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Timeout_is_failed_and_flagged(bool upgrade)
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("winget.exe", Result(0, timedOut: true));
        var client = new WingetClient(runner);
        var package = new WingetPackageRef("Git.Git");

        var result = upgrade
            ? await client.UpgradeAsync(package, TestContext.Current.CancellationToken)
            : await client.InstallAsync(package, TestContext.Current.CancellationToken);

        Assert.Equal(WingetStatus.Failed, result.Status);
        Assert.True(result.TimedOut);
        Assert.Equal(WingetStatus.Failed, WingetClient.MapExitCode(0, upgrade, timedOut: true));
    }

    [Fact]
    public async Task Install_passes_source_and_version()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("winget.exe", Result(0));

        await new WingetClient(runner).InstallAsync(
            new WingetPackageRef("Python.Python.3.12", "msstore", "3.12.7"), TestContext.Current.CancellationToken);

        var args = runner.Calls[0].Arguments.ToList();
        var exact = args.IndexOf("--exact");
        Assert.Equal(["--id", "Python.Python.3.12", "--exact"], args.GetRange(exact - 2, 3));
        Assert.True(args.IndexOf("--source") > exact);
        Assert.Equal("msstore", args[args.IndexOf("--source") + 1]);
        Assert.True(args.IndexOf("--version") > args.IndexOf("--source"));
        Assert.Equal("3.12.7", args[args.IndexOf("--version") + 1]);
    }

    [Fact]
    public async Task Upgrade_not_applicable_is_up_to_date()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("winget.exe", Result(-1978335189));

        var result = await new WingetClient(runner).UpgradeAsync(new WingetPackageRef("Git.Git"), TestContext.Current.CancellationToken);

        Assert.Equal(WingetStatus.UpToDate, result.Status);
        Assert.Equal("upgrade", runner.Calls[0].Arguments[0]);
        Assert.Equal(Timeouts.Install, runner.Calls[0].Timeout);
    }

    [Fact]
    public async Task Upgrade_success_is_upgraded()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("winget.exe", Result(0));

        var result = await new WingetClient(runner).UpgradeAsync(new WingetPackageRef("Git.Git"), TestContext.Current.CancellationToken);

        Assert.Equal(WingetStatus.Upgraded, result.Status);
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("x;y")]
    [InlineData("--force")]
    [InlineData("Git.Git\n")]
    [InlineData("Git.Git\r\n")]
    public async Task Unsafe_id_throws(string id)
    {
        var runner = new FakeProcessRunner();
        var client = new WingetClient(runner);
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentException>(async () => await client.InstallAsync(new WingetPackageRef(id), ct));
        await Assert.ThrowsAsync<ArgumentException>(async () => await client.UpgradeAsync(new WingetPackageRef(id), ct));
        await Assert.ThrowsAsync<ArgumentException>(async () => await client.IsInstalledAsync(new WingetPackageRef(id), ct));
        await Assert.ThrowsAsync<ArgumentException>(async () => await client.InstallAsync(new WingetPackageRef("Git.Git", id), ct));
        await Assert.ThrowsAsync<ArgumentException>(async () => await client.InstallAsync(new WingetPackageRef("Git.Git", null, id), ct));
        Assert.Throws<ArgumentException>(() => WingetClient.Validate(id, "id"));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task IsAvailable_true_on_exit_zero()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("winget.exe", Result(0));

        Assert.True(await new WingetClient(runner).IsAvailableAsync(TestContext.Current.CancellationToken));
        Assert.Equal(["--version"], runner.Calls[0].Arguments);
        Assert.Equal(Timeouts.Query, runner.Calls[0].Timeout);
    }

    [Fact]
    public async Task IsAvailable_false_when_winget_missing()
    {
        var runner = new FakeProcessRunner();
        runner.ThrowNotFound("winget.exe");

        Assert.False(await new WingetClient(runner).IsAvailableAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsAvailable_false_on_timeout_or_nonzero()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("winget.exe", Result(0, timedOut: true));
        runner.Enqueue("winget.exe", Result(1));
        var client = new WingetClient(runner);

        Assert.False(await client.IsAvailableAsync(TestContext.Current.CancellationToken));
        Assert.False(await client.IsAvailableAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Export_builds_arguments()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("winget.exe", Result(0));

        await new WingetClient(runner).ExportAsync(@"C:\tmp\my apps.json", TestContext.Current.CancellationToken);

        Assert.Equal(
            ["export", "-o", @"C:\tmp\my apps.json", "--accept-source-agreements", "--disable-interactivity"],
            runner.Calls[0].Arguments);
        Assert.Equal(Timeouts.Query, runner.Calls[0].Timeout);
    }

    [Fact]
    public async Task Search_returns_raw_output()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("winget.exe", new ProcessResult(0, "Name Id\nGit Git.Git", "", TimeSpan.Zero, false));

        var result = await new WingetClient(runner).SearchAsync("git hub", TestContext.Current.CancellationToken);

        Assert.Equal("Name Id\nGit Git.Git", result.StdOut);
        Assert.Equal(
            ["search", "--query", "git hub", "--accept-source-agreements", "--disable-interactivity"],
            runner.Calls[0].Arguments);
        Assert.Equal(Timeouts.Query, runner.Calls[0].Timeout);
    }

    [Fact]
    public async Task Existing_string_overloads_still_work()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("winget.exe", Result(0));
        runner.Enqueue("winget.exe", Result(0));
        var client = new WingetClient(runner);

        var install = await client.InstallAsync("Git.Git", TestContext.Current.CancellationToken, "winget");
        var installed = await client.IsInstalledAsync("Git.Git", TestContext.Current.CancellationToken, "winget");

        Assert.Equal(WingetStatus.Installed, install.Status);
        Assert.True(installed);
        Assert.Equal(["--source", "winget"], runner.Calls[0].Arguments.TakeLast(2));
        Assert.Equal(["--source", "winget"], runner.Calls[1].Arguments.TakeLast(2));
    }

    [Fact]
    public void InstallArguments_are_exact()
    {
        Assert.Equal(
            ["install", "--id", "Git.Git", "--exact", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity", "--source", "winget", "--version", "2.0"],
            WingetClient.InstallArguments(new WingetPackageRef("Git.Git", "winget", "2.0")));
    }

    [Fact]
    public void UpgradeArguments_are_exact()
    {
        Assert.Equal(
            ["upgrade", "--id", "Git.Git", "--exact", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"],
            WingetClient.UpgradeArguments(new WingetPackageRef("Git.Git")));
    }

    [Theory]
    [InlineData("machine")]
    [InlineData("user")]
    public void Scope_is_appended_after_source_and_version(string scope)
    {
        var package = new WingetPackageRef("Git.Git", "winget", "2.0", scope);

        Assert.Equal(["--source", "winget", "--version", "2.0", "--scope", scope], WingetClient.InstallArguments(package).TakeLast(6));
        Assert.Equal(["--source", "winget", "--version", "2.0", "--scope", scope], WingetClient.UpgradeArguments(package).TakeLast(6));
    }

    [Fact]
    public async Task Scope_is_not_passed_to_list()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("winget.exe", Result(0));

        await new WingetClient(runner).IsInstalledAsync(new WingetPackageRef("Git.Git", Scope: "machine"), TestContext.Current.CancellationToken);

        Assert.DoesNotContain("--scope", runner.Calls[0].Arguments);
    }

    [Theory]
    [InlineData("Machine")]
    [InlineData("system")]
    [InlineData("")]
    [InlineData("user; calc")]
    public void Unknown_scope_throws(string scope)
    {
        var package = new WingetPackageRef("Git.Git", Scope: scope);

        Assert.Throws<ArgumentException>(() => WingetClient.InstallArguments(package));
        Assert.Throws<ArgumentException>(() => WingetClient.UpgradeArguments(package));
    }
}
