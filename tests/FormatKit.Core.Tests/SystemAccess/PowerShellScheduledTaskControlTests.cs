using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class PowerShellScheduledTaskControlTests
{
    private static ProcessResult Result(int exit, string stdout = "", string stderr = "") =>
        new(exit, stdout, stderr, TimeSpan.Zero, false);

    private static (PowerShellScheduledTaskControl Control, FakeProcessRunner Runner) Create(ProcessResult result)
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("powershell.exe", result);
        return (new PowerShellScheduledTaskControl(runner), runner);
    }

    [Theory]
    [InlineData("Ready", true)]
    [InlineData("Running", true)]
    [InlineData("Disabled", false)]
    [InlineData("Absent", null)]
    public async Task IsEnabled_maps_states(string output, bool? expected)
    {
        var (control, runner) = Create(Result(0, output + "\r\n"));

        var enabled = await control.IsEnabledAsync(@"\Microsoft\Windows\X\", "O'Task", TestContext.Current.CancellationToken);

        Assert.Equal(expected, enabled);
        var script = Assert.Single(runner.Calls).Arguments[3];
        Assert.Contains(@"-TaskPath '\Microsoft\Windows\X\'", script);
        Assert.Contains("-TaskName 'O''Task'", script);
    }

    [Theory]
    [InlineData(true, "Enable-ScheduledTask")]
    [InlineData(false, "Disable-ScheduledTask")]
    public async Task SetEnabled_uses_the_matching_cmdlet(bool enabled, string cmdlet)
    {
        var (control, runner) = Create(Result(0));

        await control.SetEnabledAsync(@"\A\", "B", enabled, TestContext.Current.CancellationToken);

        var script = Assert.Single(runner.Calls).Arguments[3];
        Assert.Contains($@"{cmdlet} -TaskPath '\A\' -TaskName 'B'", script);
    }

    [Fact]
    public async Task Failure_throws()
    {
        var (control, _) = Create(Result(1, "", "boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => control.SetEnabledAsync(@"\A\", "B", false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsEnabled_fails_clearly_when_several_tasks_match()
    {
        var (control, _) = Create(Result(0, "Multiple\r\n"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => control.IsEnabledAsync(@"\A\", "B", TestContext.Current.CancellationToken));
        Assert.Contains("More than one", ex.Message);
    }
}
