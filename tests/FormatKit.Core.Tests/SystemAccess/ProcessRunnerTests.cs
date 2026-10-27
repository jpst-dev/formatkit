using FormatKit.Core.SystemAccess;
using Serilog;

namespace FormatKit.Core.Tests.SystemAccess;

[Trait("Category", "Process")]
public class ProcessRunnerTests
{
    private static ProcessRunner CreateRunner() => new(new LoggerConfiguration().CreateLogger());

    [Fact]
    public async Task Captures_exit_code_and_stdout()
    {
        var result = await CreateRunner().RunAsync(
            new ProcessSpec("cmd.exe", ["/c", "echo hello& exit 3"], TimeSpan.FromSeconds(10)),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("hello", result.StdOut.Trim());
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task Decodes_accented_output_written_in_the_oem_code_page()
    {
        var result = await CreateRunner().RunAsync(
            new ProcessSpec("cmd.exe", ["/c", "echo Utilitário"], TimeSpan.FromSeconds(10)),
            TestContext.Current.CancellationToken);

        Assert.Equal("Utilitário", result.StdOut.Trim());
    }

    [Fact]
    public async Task Kills_process_after_timeout()
    {
        var result = await CreateRunner().RunAsync(
            new ProcessSpec("powershell.exe", ["-NoProfile", "-Command", "Start-Sleep 30"], TimeSpan.FromSeconds(1)),
            TestContext.Current.CancellationToken);

        Assert.True(result.TimedOut);
        Assert.True(result.Duration < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Missing_executable_throws_FileNotFoundException()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => CreateRunner().RunAsync(
            new ProcessSpec("does-not-exist-fk.exe", [], TimeSpan.FromSeconds(5)),
            TestContext.Current.CancellationToken));
    }
}
