using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class SchtasksScheduledTasksTests
{
    private const string Exe = "C:\\Tools\\formatkit.exe";

    private static readonly string[] CliArguments = ["run", "--resume", "--wait-on-exit"];

    private static ProcessResult Result(int exit, string stdout = "", string stderr = "") =>
        new(exit, stdout, stderr, TimeSpan.Zero, false);

    [Fact]
    public async Task RegisterResumeTask_CliArguments_Unchanged()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("schtasks.exe", Result(0));

        await new SchtasksScheduledTasks(runner, Exe, CliArguments).RegisterResumeTaskAsync(TestContext.Current.CancellationToken);

        var call = Assert.Single(runner.Calls);
        Assert.Equal(
            ["/Create", "/TN", "FormatKit\\Resume", "/TR", "\"C:\\Tools\\formatkit.exe\" run --resume --wait-on-exit","/SC", "ONLOGON", "/RL", "HIGHEST", "/F"],
            call.Arguments);
    }

    [Fact]
    public async Task RegisterResumeTask_UsesGivenArguments()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("schtasks.exe", Result(0));

        await new SchtasksScheduledTasks(runner, "C:\\x\\FormatKitUI.exe", ["--resume"])
            .RegisterResumeTaskAsync(TestContext.Current.CancellationToken);

        var call = Assert.Single(runner.Calls);
        Assert.Equal("\"C:\\x\\FormatKitUI.exe\" --resume", call.Arguments[call.Arguments.ToList().IndexOf("/TR") + 1]);
    }

    [Fact]
    public async Task RegisterResumeTask_QuotesArgumentsContainingSpaces()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("schtasks.exe", Result(0));

        await new SchtasksScheduledTasks(runner, Exe, ["--profile", "C:\\my dir\\p.json"])
            .RegisterResumeTaskAsync(TestContext.Current.CancellationToken);

        var call = Assert.Single(runner.Calls);
        Assert.Equal("\"C:\\Tools\\formatkit.exe\" --profile \"C:\\my dir\\p.json\"", call.Arguments[call.Arguments.ToList().IndexOf("/TR") + 1]);
    }

    [Fact]
    public async Task Register_failure_throws_with_stderr()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("schtasks.exe", Result(1, stderr: "ERROR: Access is denied."));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SchtasksScheduledTasks(runner, Exe, CliArguments).RegisterResumeTaskAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Access is denied", ex.Message);
    }

    [Fact]
    public async Task Register_timeout_throws()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("schtasks.exe", new ProcessResult(0, "", "", TimeSpan.Zero, true));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SchtasksScheduledTasks(runner, Exe, CliArguments).RegisterResumeTaskAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Remove_queries_then_deletes_when_task_present()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("schtasks.exe", Result(0));
        runner.Enqueue("schtasks.exe", Result(0));

        var removed = await new SchtasksScheduledTasks(runner, Exe, CliArguments).RemoveResumeTaskAsync(TestContext.Current.CancellationToken);

        Assert.True(removed);
        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal(["/Query", "/TN", "FormatKit\\Resume"], runner.Calls[0].Arguments);
        Assert.Equal(["/Delete", "/TN", "FormatKit\\Resume", "/F"], runner.Calls[1].Arguments);
    }

    [Fact]
    public async Task Remove_does_nothing_when_query_reports_missing_in_localized_text()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("schtasks.exe", Result(1, stderr: "ERRO: O sistema nao pode encontrar o arquivo especificado."));

        var removed = await new SchtasksScheduledTasks(runner, Exe, CliArguments).RemoveResumeTaskAsync(TestContext.Current.CancellationToken);

        Assert.False(removed);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("/Query", call.Arguments[0]);
    }

    [Fact]
    public async Task Remove_delete_failure_throws()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("schtasks.exe", Result(0));
        runner.Enqueue("schtasks.exe", Result(1, stderr: "ERRO: Acesso negado."));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SchtasksScheduledTasks(runner, Exe, CliArguments).RemoveResumeTaskAsync(TestContext.Current.CancellationToken));
    }
}
