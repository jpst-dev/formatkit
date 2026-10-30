using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Cleanup.Tests.Actions;

public class ScheduledTaskHandlerTests
{
    private const string Folder = @"\Microsoft\Windows\Application Experience\";
    private const string TaskName = "ProgramDataUpdater";
    private static readonly ScheduledTaskAction Disable = new(Folder, TaskName, false);

    private static (ScheduledTaskHandler Handler, FakeScheduledTaskControl Tasks, Core.Steps.StepContext Ctx) Create(bool? enabled)
    {
        var tasks = new FakeScheduledTaskControl();
        if (enabled is { } e)
        {
            tasks.Tasks[Folder + TaskName] = e;
        }

        return (new ScheduledTaskHandler(), tasks, TestServices.Context(TestServices.Create(taskControl: tasks)));
    }

    [Fact]
    public async Task Is_applied_when_state_matches()
    {
        var (handler, _, ctx) = Create(enabled: false);
        Assert.True(await handler.IsAppliedAsync(Disable, ctx, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Is_not_applied_when_state_differs()
    {
        var (handler, _, ctx) = Create(enabled: true);
        Assert.False(await handler.IsAppliedAsync(Disable, ctx, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Absent_task_is_not_applied_and_capture_reports_not_present()
    {
        var ct = TestContext.Current.CancellationToken;
        var (handler, _, ctx) = Create(enabled: null);

        Assert.False(await handler.IsAppliedAsync(Disable, ctx, ct));
        await Assert.ThrowsAsync<TargetNotPresentException>(() => handler.CaptureAsync(Disable, ctx, ct));
    }

    [Fact]
    public async Task Apply_journals_previous_state_and_verifies()
    {
        var ct = TestContext.Current.CancellationToken;
        var (handler, tasks, ctx) = Create(enabled: true);

        var captured = await handler.CaptureAsync(Disable, ctx, ct);
        Assert.Equal(Folder + TaskName, captured.Target);
        Assert.True(captured.Previous.GetProperty("enabled").GetBoolean());

        await handler.ApplyAsync(Disable, captured, ctx, ct);
        Assert.False(tasks.Tasks[Folder + TaskName]);
    }

    [Fact]
    public async Task Undo_restores_previous_state()
    {
        var ct = TestContext.Current.CancellationToken;
        var (handler, tasks, ctx) = Create(enabled: true);
        var captured = await handler.CaptureAsync(Disable, ctx, ct);
        var recorded = await handler.ApplyAsync(Disable, captured, ctx, ct);

        await handler.UndoAsync(recorded, ctx, ct);

        Assert.True(tasks.Tasks[Folder + TaskName]);
    }

    [Fact]
    public async Task Undo_when_already_at_previous_state_succeeds_without_changes()
    {
        var ct = TestContext.Current.CancellationToken;
        var (handler, tasks, ctx) = Create(enabled: true);
        var captured = await handler.CaptureAsync(Disable, ctx, ct);
        var recorded = await handler.ApplyAsync(Disable, captured, ctx, ct);
        await handler.UndoAsync(recorded, ctx, ct);
        tasks.Changes.Clear();

        await handler.UndoAsync(recorded, ctx, ct);

        Assert.Empty(tasks.Changes);
        Assert.True(tasks.Tasks[Folder + TaskName]);
    }
}
