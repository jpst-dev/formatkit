using FormatKit.Core;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Drivers.Catalog;
using FormatKit.Drivers.Steps;

namespace FormatKit.Drivers.Tests.Steps;

public class OemToolStepTests
{
    private static readonly VendorCatalog Catalog = VendorCatalog.LoadEmbedded();
    private static readonly string DellExe = Environment.ExpandEnvironmentVariables(Catalog.Oem.Single(o => o.Id == "dell").Executable);

    private static ProcessResult Result(int exit, bool timedOut = false) => new(exit, "", "", TimeSpan.Zero, timedOut);

    private static (OemToolStep Step, FakeProcessRunner Runner, StepContext Ctx) Arrange(string manufacturer = "Dell Inc.")
    {
        var runner = new FakeProcessRunner();
        var hw = new FakeHardwareInfo(FakeHardwareInfo.Sample(manufacturer: manufacturer));
        return (new OemToolStep(Catalog), runner, TestServices.Context(TestServices.Create(runner, hw)));
    }

    [Fact]
    public void Has_expected_metadata()
    {
        var step = new OemToolStep(Catalog);

        Assert.Equal("drivers.oem-tool", step.Id);
        Assert.True(step.RequiresAdmin);
        Assert.False(string.IsNullOrWhiteSpace(step.Title));
    }

    [Fact]
    public async Task Skips_custom_build()
    {
        var (step, runner, ctx) = Arrange("Contoso");

        Assert.True(await step.CheckAsync(ctx, TestContext.Current.CancellationToken));
        var result = await step.ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Skipped, result.Outcome);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Check_false_for_oem_machine()
    {
        var (step, _, ctx) = Arrange();

        Assert.False(await step.CheckAsync(ctx, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Installs_tool_then_runs_it()
    {
        var (step, runner, ctx) = Arrange();
        runner.Enqueue("winget.exe", Result(0));
        runner.Enqueue(DellExe, Result(0));

        var result = await step.ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Equal(2, runner.Calls.Count);
        Assert.Contains("Dell.CommandUpdate", runner.Calls[0].Arguments);
        Assert.Equal(DellExe, runner.Calls[1].FileName);
        Assert.Contains("/applyUpdates", runner.Calls[1].Arguments);
        Assert.Equal(Timeouts.Install, runner.Calls[1].Timeout);
    }

    [Fact]
    public async Task Executable_is_expanded_before_running()
    {
        var (step, runner, ctx) = Arrange();
        runner.Enqueue("winget.exe", Result(0));
        runner.Enqueue(DellExe, Result(0));

        await step.ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.DoesNotContain('%', runner.Calls[1].FileName);
    }

    [Fact]
    public async Task Already_installed_tool_still_runs()
    {
        var (step, runner, ctx) = Arrange();
        runner.Enqueue("winget.exe", Result(-1978335189));
        runner.Enqueue(DellExe, Result(0));

        var result = await step.ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task Tool_reboot_exit_code_returns_RebootRequired()
    {
        var (step, runner, ctx) = Arrange();
        runner.Enqueue("winget.exe", Result(0));
        runner.Enqueue(DellExe, Result(1));

        var result = await step.ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.RebootRequired, result.Outcome);
    }

    [Fact]
    public async Task Tool_success_exit_code_500_succeeds()
    {
        var (step, runner, ctx) = Arrange();
        runner.Enqueue("winget.exe", Result(0));
        runner.Enqueue(DellExe, Result(500));

        var result = await step.ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
    }

    [Fact]
    public async Task Unknown_exit_code_fails_with_code_in_message()
    {
        var (step, runner, ctx) = Arrange();
        runner.Enqueue("winget.exe", Result(0));
        runner.Enqueue(DellExe, Result(42));

        var result = await step.ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Contains(result.Messages, m => m.Contains("42"));
    }

    [Fact]
    public async Task Tool_timeout_fails()
    {
        var (step, runner, ctx) = Arrange();
        runner.Enqueue("winget.exe", Result(0));
        runner.Enqueue(DellExe, Result(0, timedOut: true));

        var result = await step.ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.NotEmpty(result.Messages);
    }

    [Fact]
    public async Task Winget_failure_fails_without_running_tool()
    {
        var (step, runner, ctx) = Arrange();
        runner.Enqueue("winget.exe", Result(1));

        var result = await step.ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task Winget_missing_fails_with_app_installer_hint()
    {
        var (step, runner, ctx) = Arrange();
        runner.ThrowNotFound("winget.exe");

        var result = await step.ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.NextAction));
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task Hardware_is_read_at_run_time()
    {
        var runner = new FakeProcessRunner();
        var hw = new FakeHardwareInfo(FakeHardwareInfo.Sample(manufacturer: "Contoso"));
        var step = new OemToolStep(Catalog);
        var ctx = TestServices.Context(TestServices.Create(runner, hw));

        hw.Snapshot = FakeHardwareInfo.Sample(manufacturer: "Dell Inc.");

        Assert.False(await step.CheckAsync(ctx, TestContext.Current.CancellationToken));
    }
}
