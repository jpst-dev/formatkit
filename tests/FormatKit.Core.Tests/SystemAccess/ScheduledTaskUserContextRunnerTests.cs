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
    public void Script_converts_unsigned_result_to_int32()
    {
        var script = ScheduledTaskUserContextRunner.BuildScript(new ProcessSpec("a.exe", [], TimeSpan.FromMinutes(1)));
        Assert.Contains("[BitConverter]::ToInt32([BitConverter]::GetBytes([uint32]$info.LastTaskResult),0)", script);
        Assert.Contains("Stop-ScheduledTask", script);
        Assert.Contains("-AllowStartIfOnBatteries", script);
    }

    [Theory]
    [InlineData("C:\\Program Files\\X", "\"C:\\Program Files\\X\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData("C:\\My Dir\\", "\"C:\\My Dir\\\\\"")]
    [InlineData("plain", "plain")]
    public void QuoteArgument_follows_windows_rules(string input, string expected)
    {
        Assert.Equal(expected, ScheduledTaskUserContextRunner.QuoteArgument(input));
    }

    [Fact]
    public void Quote_doubles_all_single_quote_characters()
    {
        Assert.Equal(
            "'a''b\u2018\u2018\u2019\u2019\u201A\u201A\u201B\u201B'",
            ScheduledTaskUserContextRunner.Quote("a'b\u2018\u2019\u201A\u201B"));
    }

    [Fact]
    public void Script_has_finally_attached_to_try()
    {
        var script = ScheduledTaskUserContextRunner.BuildScript(new ProcessSpec("a.exe", [], TimeSpan.FromMinutes(1)));
        Assert.Contains("} finally{", script);
        Assert.DoesNotContain("}; finally", script);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Script_parses_without_errors()
    {
        var script = ScheduledTaskUserContextRunner.BuildScript(
            new ProcessSpec(@"C:\Program Files\X\app it's.exe", ["--name", "it's \u2019 x"], TimeSpan.FromMinutes(1)));
        var tmp = Path.Combine(Path.GetTempPath(), $"fk-script-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(tmp, script, new System.Text.UTF8Encoding(true), TestContext.Current.CancellationToken);
        try
        {
            var result = await new ProcessRunner(Serilog.Core.Logger.None).RunAsync(
                new ProcessSpec(
                    "powershell.exe",
                    ["-NoProfile", "-NonInteractive", "-Command",
                     $"$e=$null; [void][System.Management.Automation.Language.Parser]::ParseFile('{tmp}', [ref]$null, [ref]$e); $e.Count"],
                    TimeSpan.FromMinutes(1)),
                CancellationToken.None);
            Assert.Equal("0", result.StdOut.Trim());
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task Outer_timeout_returns_timed_out()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue("powershell.exe", new ProcessResult(-1, "", "", TimeSpan.Zero, true));
        var result = await new ScheduledTaskUserContextRunner(fake)
            .RunAsync(new ProcessSpec("a.exe", [], TimeSpan.FromSeconds(1)), CancellationToken.None);
        Assert.True(result.TimedOut);
    }

    [Fact]
    public async Task Script_quotes_arguments()
    {
        var (_, fake) = await Run("0", "it's");
        Assert.Contains("it''s", fake.Calls[0].Arguments[3]);
    }
}
