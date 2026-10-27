using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Drivers.Steps;

namespace FormatKit.Drivers.Tests.Steps;

public class NetworkCheckStepTests
{
    private const string Dir = @"C:\backup\net";

    private static ProcessResult Exit(int code) => new(code, "", "", TimeSpan.Zero, false);

    private static (StepContext Ctx, FakeNetworkProbe Probe, FakeProcessRunner Procs) Arrange(
        bool dirExists, int pnpExit, params bool[] probe)
    {
        var procs = new FakeProcessRunner();
        procs.Enqueue("pnputil.exe", Exit(pnpExit));
        var fs = new InMemoryFileSystem();
        if (dirExists)
        {
            fs.CreateDirectory(Dir);
        }

        var net = new FakeNetworkProbe(probe);
        return (TestServices.Context(TestServices.Create(processes: procs, network: net, files: fs)), net, procs);
    }

    [Fact]
    public void Step_is_critical()
    {
        var step = new NetworkCheckStep(Dir, TimeSpan.Zero);

        Assert.Equal("drivers.network-check", step.Id);
        Assert.True(step.IsCritical);
        Assert.True(step.RequiresAdmin);
    }

    [Fact]
    public async Task Check_true_when_online()
    {
        var (ctx, _, _) = Arrange(true, 0, true);

        Assert.True(await new NetworkCheckStep(Dir, TimeSpan.Zero).CheckAsync(ctx, CancellationToken.None));
    }

    [Fact]
    public async Task No_backup_configured_fails_with_next_action()
    {
        var (ctx, _, procs) = Arrange(false, 0, false);

        var result = await new NetworkCheckStep(null, TimeSpan.Zero).ExecuteAsync(ctx, CancellationToken.None);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Contains("networkDriverBackup", result.NextAction);
        Assert.Empty(procs.Calls);
    }

    [Fact]
    public async Task Missing_backup_directory_fails()
    {
        var (ctx, _, procs) = Arrange(false, 0, false);

        var result = await new NetworkCheckStep(Dir, TimeSpan.Zero).ExecuteAsync(ctx, CancellationToken.None);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Contains(Dir, result.Messages[0]);
        Assert.Empty(procs.Calls);
    }

    [Fact]
    public async Task Installs_backup_and_succeeds_when_online_after()
    {
        var (ctx, _, procs) = Arrange(true, 0, false, true);
        var step = new NetworkCheckStep(Dir, TimeSpan.Zero);

        Assert.False(await step.CheckAsync(ctx, CancellationToken.None));
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        var call = Assert.Single(procs.Calls);
        Assert.Contains("/add-driver", call.Arguments);
        Assert.Contains(Path.Combine(Dir, "*.inf"), call.Arguments);
    }

    [Fact]
    public async Task Still_offline_after_install_fails()
    {
        var (ctx, probe, _) = Arrange(true, 0, false);

        var result = await new NetworkCheckStep(Dir, TimeSpan.Zero).ExecuteAsync(ctx, CancellationToken.None);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(3, probe.CallCount);
        Assert.False(string.IsNullOrWhiteSpace(result.NextAction));
    }

    [Fact]
    public async Task Pnputil_failure_fails_with_exit_code_without_probing()
    {
        var (ctx, probe, _) = Arrange(true, 5, true);

        var result = await new NetworkCheckStep(Dir, TimeSpan.Zero).ExecuteAsync(ctx, CancellationToken.None);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Contains("5", result.Messages[0]);
        Assert.Equal(0, probe.CallCount);
    }
}
