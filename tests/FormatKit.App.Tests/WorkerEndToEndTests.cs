using FormatKit.App.Worker;
using Serilog;

namespace FormatKit.App.Tests;

/// <summary>
/// Starts the built FormatKit.exe as a worker, without elevation, with a job that fails at once (the profile does not
/// exist): proves the pipe connects across processes and the job's arguments arrive whole. Changes nothing on the
/// machine; the worker writes its own log file in the usual logs folder.
/// </summary>
[Trait("Category", "Integration")]
public class WorkerEndToEndTests
{
    [Fact]
    public async Task Worker_MissingProfile_SendsFailedAndExitsWithTwo()
    {
        var ct = TestContext.Current.CancellationToken;
        var exe = Path.Combine(AppContext.BaseDirectory, "FormatKit.exe");
        var profile = Path.Combine(Path.GetTempPath(), "Perfis de teste " + Guid.NewGuid().ToString("N"), "ação.json");
        var launcher = new WorkerLauncher(new ElevationStarter(), new LoggerConfiguration().CreateLogger(), exe);

        var connection = await launcher.StartAsync(["run", "--profile", profile, "--profile-hash", "x"], elevate: false, ct);

        Assert.NotNull(connection);
        await using var channel = connection.Channel;
        var messages = new List<WorkerMessage>();
        await foreach (var message in channel.ReadAllAsync(ct).WithCancellation(ct)) messages.Add(message);
        var failed = Assert.IsType<Failed>(Assert.Single(messages));
        Assert.Contains("ação.json", failed.Message);
        Assert.Equal(WorkerHost.ExitFailed, await connection.Exited.WaitAsync(TimeSpan.FromSeconds(30), ct));
    }
}
