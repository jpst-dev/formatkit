using FormatKit.Prepare.Localization;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace FormatKit.Prepare.Tests;

public class PrepareChecklistTests
{
    private sealed class Check(string id, PrepareGroup group, Func<IReadOnlyList<PrepareItem>> run) : IPrepareCheck
    {
        public string Id => id;
        public PrepareGroup Group => group;
        public Task<IReadOnlyList<PrepareItem>> RunAsync(PrepareContext context, CancellationToken ct) => Task.FromResult(run());
    }

    private sealed class Sink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static PrepareItem Item(string id, PrepareGroup g, PrepareStatus s, string title = "t") => new(id, g, s, title);

    [Fact]
    public async Task ThrowingCheckBecomesNotCheckedAndNextCheckRuns()
    {
        var sink = new Sink();
        var ctx = PrepareTestContext.Create(log: new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger());
        var checklist = new PrepareChecklist([
            new Check("a.boom", PrepareGroup.Data, () => throw new IOException("disk")),
            new Check("b.ok", PrepareGroup.Data, () => [Item("b.ok", PrepareGroup.Data, PrepareStatus.Ok)])]);

        var report = await checklist.RunAsync(ctx, CancellationToken.None);

        Assert.Equal(2, report.Items.Count);
        var notChecked = report.Items.Single(i => i.CheckId == "a.boom");
        Assert.Equal(PrepareStatus.NotChecked, notChecked.Status);
        Assert.Equal(PrepareGroup.Data, notChecked.Group);
        Assert.Equal(Strings.Get("Item_NotChecked_Title"), notChecked.Title);
        Assert.Contains(sink.Events, e => e.Exception is IOException);
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        var checklist = new PrepareChecklist([new Check("a", PrepareGroup.Data, () => throw new OperationCanceledException())]);
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => checklist.RunAsync(PrepareTestContext.Create(), CancellationToken.None));
    }

    [Fact]
    public async Task SortsByStatusGroupCheckIdKeepingItemOrder()
    {
        var checklist = new PrepareChecklist([
            new Check("z", PrepareGroup.Security, () => [Item("z", PrepareGroup.Security, PrepareStatus.Ok)]),
            new Check("b", PrepareGroup.Apps, () => [Item("b", PrepareGroup.Apps, PrepareStatus.Blocker)]),
            new Check("a", PrepareGroup.Data, () => [Item("a", PrepareGroup.Data, PrepareStatus.Blocker, "first"), Item("a", PrepareGroup.Data, PrepareStatus.Blocker, "second")]),
            new Check("c", PrepareGroup.Data, () => [Item("c", PrepareGroup.Data, PrepareStatus.Blocker)])]);

        var report = await checklist.RunAsync(PrepareTestContext.Create(), CancellationToken.None);

        Assert.Equal(["a:first", "a:second", "c:t", "b:t", "z:t"], report.Items.Select(i => $"{i.CheckId}:{i.Title}"));
    }

    [Fact]
    public async Task HasBlockersOnlyWithABlocker()
    {
        var withBlocker = await new PrepareChecklist([new Check("a", PrepareGroup.Data, () => [Item("a", PrepareGroup.Data, PrepareStatus.Blocker)])])
            .RunAsync(PrepareTestContext.Create(), CancellationToken.None);
        var without = await new PrepareChecklist([new Check("a", PrepareGroup.Data, () => [Item("a", PrepareGroup.Data, PrepareStatus.Attention)])])
            .RunAsync(PrepareTestContext.Create(), CancellationToken.None);
        Assert.True(withBlocker.HasBlockers);
        Assert.False(without.HasBlockers);
    }
}
