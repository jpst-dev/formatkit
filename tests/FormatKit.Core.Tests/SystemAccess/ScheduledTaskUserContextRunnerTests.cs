using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class ScheduledTaskUserContextRunnerTests
{
    private static async Task<(ProcessResult Result, FakeProcessRunner Fake)> Run(string stdout, params string[] args)
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue("powershell.exe", new ProcessResult(0, stdout, string.Empty, TimeSpan.Zero, false));
        var runner = new ScheduledTaskUserContextRunner(fake);
        var result = await runner.RunAsync(new ProcessSpec(@"C:\x\app.exe", args, TimeSpan.FromMinutes(5)), CancellationToken.None);
        return (result, fake);
    }

    [Fact]
    public async Task Runs_script_and_returns_printed_exit_code()
    {
        var (result, fake) = await Run("0\r\n");
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        var call = Assert.Single(fake.Calls);
        Assert.Equal(TimeSpan.FromMinutes(7), call.Timeout);
        Assert.Contains("Register-ScheduledTask", call.Arguments[3]);
        Assert.Contains("-RunLevel Limited", call.Arguments[3]);
    }

    [Fact]
    public async Task Nonzero_task_result_is_returned()
    {
        var (result, _) = await Run("-1978335212");
        Assert.Equal(-1978335212, result.ExitCode);
    }

    [Fact]
    public async Task Timeout_marker_sets_timed_out()
    {
        var (result, _) = await Run("TIMEOUT\r\n");
        Assert.True(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    public async Task Unexpected_output_throws_invalid_operation()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run("garbage"));
    }

    [Fact]
    public async Task Script_quotes_arguments()
    {
        var (_, fake) = await Run("0", "it's");
        Assert.Contains("it''s", fake.Calls[0].Arguments[3]);
    }
}
