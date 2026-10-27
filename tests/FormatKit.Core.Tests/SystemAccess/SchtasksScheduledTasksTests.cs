using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class SchtasksScheduledTasksTests
{
    private const string Exe = "C:\\Tools\\formatkit.exe";

    private static ProcessResult Result(int exit, string stdout = "", string stderr = "") =>
        new(exit, stdout, stderr, TimeSpan.Zero, false);

    [Fact]
    public async Task Register_uses_onlogon_highest_and_quoted_exe()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("schtasks.exe", Result(0));

        await new SchtasksScheduledTasks(runner, Exe).RegisterResumeTaskAsync(TestContext.Current.CancellationToken);

        var call = Assert.Single(runner.Calls);
        Assert.Equal(
            ["/Create", "/TN", "FormatKit\\Resume", "/TR", "\"C:\\Tools\\formatkit.exe\" run --resume", "/SC", "ONLOGON", "/RL", "HIGHEST", "/F"],
            call.Arguments);
    }

    [Fact]
    public async Task Register_failure_throws_with_stderr()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("schtasks.exe", Result(1, stderr: "ERROR: Access is denied."));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SchtasksScheduledTasks(runner, Exe).RegisterResumeTaskAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Access is denied", ex.Message);
    }

    [Fact]
    public async Task Remove_ignores_missing_task()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("schtasks.exe", Result(1, stderr: "ERROR: The system cannot find the file specified."));

        await new SchtasksScheduledTasks(runner, Exe).RemoveResumeTaskAsync(TestContext.Current.CancellationToken);

        var call = Assert.Single(runner.Calls);
        Assert.Equal(["/Delete", "/TN", "FormatKit\\Resume", "/F"], call.Arguments);
    }
}
