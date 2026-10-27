using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using Serilog;

namespace FormatKit.Core.Tests.SystemAccess;

public class ConsoleRebooterTests
{
    private static ConsoleRebooter Create(FakeProcessRunner runner, bool esc = false) =>
        new(runner, new LoggerConfiguration().CreateLogger(), () => esc, (_, _) => Task.CompletedTask, _ => { });

    private static ProcessResult Result(int exit, bool timedOut = false) =>
        new(exit, "", "boom", TimeSpan.Zero, timedOut);

    [Fact]
    public async Task Shutdown_success_returns_true_with_expected_args()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("shutdown.exe", Result(0));

        Assert.True(await Create(runner).RebootAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
        Assert.Equal(["/r", "/t", "0"], Assert.Single(runner.Calls).Arguments);
    }

    [Fact]
    public async Task Shutdown_non_zero_returns_false()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("shutdown.exe", Result(5));

        Assert.False(await Create(runner).RebootAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Shutdown_timeout_returns_false()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("shutdown.exe", Result(0, timedOut: true));

        Assert.False(await Create(runner).RebootAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Shutdown_missing_returns_false()
    {
        var runner = new FakeProcessRunner();
        runner.ThrowNotFound("shutdown.exe");

        Assert.False(await Create(runner).RebootAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Esc_cancels_without_calling_shutdown()
    {
        var runner = new FakeProcessRunner();

        Assert.False(await Create(runner, esc: true).RebootAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));
        Assert.Empty(runner.Calls);
    }
}
