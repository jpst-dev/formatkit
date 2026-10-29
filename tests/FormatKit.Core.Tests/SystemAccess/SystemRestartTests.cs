using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class SystemRestartTests
{
    private static readonly Serilog.ILogger Log = new Serilog.LoggerConfiguration().CreateLogger();

    [Fact]
    public async Task ExitZero_ReturnsTrue()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("shutdown.exe", new ProcessResult(0, "", "", TimeSpan.Zero, false));

        Assert.True(await SystemRestart.RestartAsync(runner, Log, TestContext.Current.CancellationToken));
        Assert.Equal(["/r", "/t", "0"], Assert.Single(runner.Calls).Arguments);
    }

    [Fact]
    public async Task NonZeroExit_ReturnsFalse()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("shutdown.exe", new ProcessResult(5, "", "denied", TimeSpan.Zero, false));

        Assert.False(await SystemRestart.RestartAsync(runner, Log, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MissingShutdown_ReturnsFalse()
    {
        var runner = new FakeProcessRunner();
        runner.ThrowNotFound("shutdown.exe");

        Assert.False(await SystemRestart.RestartAsync(runner, Log, TestContext.Current.CancellationToken));
    }
}
