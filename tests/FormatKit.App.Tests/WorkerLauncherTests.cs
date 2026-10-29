using FormatKit.App.Tests.Fakes;
using FormatKit.App.Worker;

namespace FormatKit.App.Tests;

public class WorkerLauncherTests
{
    private const string Exe = @"C:\Program Files\FormatKit\FormatKit.exe";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Serilog.ILogger Log => new Serilog.LoggerConfiguration().CreateLogger();

    [Fact]
    public async Task StartAsync_BuildsArgsWithPathWithSpacesAsOneArgument()
    {
        var starter = new FakeElevationStarter();
        var launcher = new WorkerLauncher(starter, Log, Exe);
        const string path = @"C:\Perfis de teste\ação.json";

        var connection = await launcher.StartAsync(["run", "--profile", path, "--profile-hash", "abc"], elevate: true, Ct);

        Assert.NotNull(connection);
        await using var _ = connection.Channel;
        var (exe, args, runas) = Assert.Single(starter.Starts);
        Assert.Equal(Exe, exe);
        Assert.True(runas);
        Assert.Equal(8, args.Count);
        Assert.Equal("--worker", args[0]);
        Assert.Equal("--pipe", args[1]);
        Assert.Matches("^FormatKit-[0-9a-f]{32}$", args[2]);
        Assert.Equal(["run", "--profile", path, "--profile-hash", "abc"], args.Skip(3));
        await using var worker = await starter.Accepted!;
        Assert.NotNull(worker);
    }

    [Fact]
    public async Task StartAsync_UsesADifferentPipeNameEachTime()
    {
        var starter = new FakeElevationStarter();
        var launcher = new WorkerLauncher(starter, Log, Exe);

        await using var a = (await launcher.StartAsync(["run", "--resume"], false, Ct))!.Channel;
        await using var b = (await launcher.StartAsync(["run", "--resume"], false, Ct))!.Channel;

        Assert.NotEqual(starter.Starts[0].Args[2], starter.Starts[1].Args[2]);
    }

    [Fact]
    public async Task StartAsync_UacDeclined_ReturnsNull()
    {
        var starter = new FakeElevationStarter { Decline = true };
        var launcher = new WorkerLauncher(starter, Log, Exe);

        var connection = await launcher.StartAsync(["run", "--resume"], elevate: true, Ct);

        Assert.Null(connection);
        Assert.True(Assert.Single(starter.Starts).Runas);
    }

    [Fact]
    public async Task StartAsync_NoElevationNeeded_DoesNotUseRunas()
    {
        var starter = new FakeElevationStarter();
        var launcher = new WorkerLauncher(starter, Log, Exe);

        var connection = await launcher.StartAsync(["run", "--resume"], elevate: false, Ct);

        Assert.NotNull(connection);
        await using var _ = connection.Channel;
        Assert.False(Assert.Single(starter.Starts).Runas);
    }

    [Fact]
    public async Task StartAsync_ExitedFollowsTheProcess()
    {
        var starter = new FakeElevationStarter();
        var launcher = new WorkerLauncher(starter, Log, Exe);

        var connection = await launcher.StartAsync(["run", "--resume"], false, Ct);
        await using var _ = connection!.Channel;
        Assert.False(connection.Exited.IsCompleted);

        starter.Process!.Exit(2);

        Assert.Equal(2, await connection.Exited.WaitAsync(TimeSpan.FromSeconds(5), Ct));
    }

    [Fact]
    public async Task StartAsync_WorkerExitsBeforeConnecting_Throws()
    {
        var launcher = new WorkerLauncher(new NeverListeningStarter(), Log, Exe, connectTimeout: TimeSpan.FromSeconds(10));

        var start = launcher.StartAsync(["run", "--resume"], false, Ct);

        var ex = await Assert.ThrowsAsync<IOException>(() => start.WaitAsync(TimeSpan.FromSeconds(5), Ct));
        Assert.Contains("7", ex.Message);
    }

    /// <summary>A worker that exits with 7 without ever creating the pipe.</summary>
    private sealed class NeverListeningStarter : IElevationStarter
    {
        public Task<IStartedProcess?> StartAsync(string exe, IReadOnlyList<string> args, bool runas)
        {
            var process = new FakeStartedProcess();
            process.Exit(7);
            return Task.FromResult<IStartedProcess?>(process);
        }
    }
}
