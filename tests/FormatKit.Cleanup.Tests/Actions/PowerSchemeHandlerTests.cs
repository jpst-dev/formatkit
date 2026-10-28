using System.Text.Json;
using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Cleanup.Tests.Actions;

public class PowerSchemeHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static PowerSchemeHandler NewHandler(params Guid[] created) =>
        new(() => created.ToDictionary(g => g, _ => (Guid?)null));

    private static PowerSchemeHandler NewHandler(Guid created, Guid source) =>
        new(() => new Dictionary<Guid, Guid?> { [created] = source });

    [Fact]
    public void Created_schemes_come_from_unreverted_power_entries()
    {
        static JournalAction Power(Guid? created) => new(PowerSchemeHandler.Type, "power",
            JsonSerializer.SerializeToElement(new { previous = Guid.NewGuid().ToString(), created = created?.ToString(), source = BuiltInPowerSchemes.Ultimate.ToString() }));
        var kept = Guid.NewGuid();
        var reverted = Guid.NewGuid();
        var journal = new CleanupJournal(1,
        [
            new JournalEntry("performance.a", DateTimeOffset.Now, [Power(kept), Power(null)], null),
            new JournalEntry("performance.b", DateTimeOffset.Now, [Power(reverted)], DateTimeOffset.Now),
        ]);

        var created = PowerSchemeHandler.CreatedSchemes(journal);
        Assert.Equal([kept], created.Keys);
        Assert.Equal(BuiltInPowerSchemes.Ultimate, created[kept]);
        Assert.Empty(PowerSchemeHandler.CreatedSchemes(null));
    }

    [Fact]
    public async Task Apply_activates_existing_builtin()
    {
        var power = new FakePowerSchemes();
        power.Schemes.Add(BuiltInPowerSchemes.HighPerformance);
        var ctx = TestServices.Context(TestServices.Create(power: power));
        var handler = NewHandler();
        var action = new PowerSchemeAction("highPerformance");

        var captured = await handler.CaptureAsync(action, ctx, Ct);
        var recorded = await handler.ApplyAsync(action, captured, ctx, Ct);

        Assert.Equal(BuiltInPowerSchemes.HighPerformance, power.Active);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, recorded.Previous.GetProperty("created").ValueKind);
        Assert.Equal(BuiltInPowerSchemes.Balanced.ToString(), recorded.Previous.GetProperty("previous").GetString());
    }

    [Fact]
    public async Task Apply_duplicates_hidden_scheme_and_records_created()
    {
        var power = new FakePowerSchemes();
        var ctx = TestServices.Context(TestServices.Create(power: power));
        var handler = NewHandler();
        var action = new PowerSchemeAction("ultimate");

        var captured = await handler.CaptureAsync(action, ctx, Ct);
        var recorded = await handler.ApplyAsync(action, captured, ctx, Ct);

        var created = Guid.Parse(recorded.Previous.GetProperty("created").GetString()!);
        Assert.NotEqual(BuiltInPowerSchemes.Ultimate, created);
        Assert.Equal(created, power.Active);
        Assert.Contains(created, power.Schemes);
        Assert.Equal(BuiltInPowerSchemes.Ultimate.ToString(), recorded.Previous.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Created_scheme_counts_as_applied_only_for_its_source()
    {
        var created = Guid.NewGuid();
        var power = new FakePowerSchemes { Active = created };
        power.Schemes.Add(created);
        var ctx = TestServices.Context(TestServices.Create(power: power));
        var handler = NewHandler(created, BuiltInPowerSchemes.HighPerformance);

        Assert.True(await handler.IsAppliedAsync(new PowerSchemeAction("highPerformance"), ctx, Ct));
        Assert.False(await handler.IsAppliedAsync(new PowerSchemeAction("ultimate"), ctx, Ct));
    }

    [Fact]
    public async Task Undo_restores_previous_and_deletes_created()
    {
        var power = new FakePowerSchemes();
        var ctx = TestServices.Context(TestServices.Create(power: power));
        var handler = NewHandler();
        var action = new PowerSchemeAction("ultimate");
        var captured = await handler.CaptureAsync(action, ctx, Ct);
        var recorded = await handler.ApplyAsync(action, captured, ctx, Ct);
        var created = power.Active;

        await handler.UndoAsync(recorded, ctx, Ct);

        Assert.Equal(BuiltInPowerSchemes.Balanced, power.Active);
        Assert.Equal([created], power.Deleted);
    }

    [Fact]
    public async Task Undo_succeeds_after_created_scheme_was_deleted_externally()
    {
        var power = new FakePowerSchemes();
        var ctx = TestServices.Context(TestServices.Create(power: power));
        var handler = NewHandler();
        var action = new PowerSchemeAction("ultimate");
        var recorded = await handler.ApplyAsync(action, await handler.CaptureAsync(action, ctx, Ct), ctx, Ct);
        var created = power.Active;
        await power.SetActiveAsync(BuiltInPowerSchemes.Balanced, Ct);
        await power.DeleteAsync(created, Ct);

        await handler.UndoAsync(recorded, ctx, Ct);

        Assert.Equal(BuiltInPowerSchemes.Balanced, power.Active);
        Assert.Equal([created], power.Deleted);
    }

    [Fact]
    public async Task Undo_activates_balanced_when_previous_is_gone()
    {
        var gone = Guid.NewGuid();
        var created = Guid.NewGuid();
        var power = new FakePowerSchemes { Active = created };
        power.Schemes.Add(created);
        var sink = new CollectingSink();
        var ctx = new Core.Steps.StepContext(TestServices.Create(power: power), new Serilog.LoggerConfiguration().WriteTo.Sink(sink).CreateLogger());
        var recorded = new JournalAction(PowerSchemeHandler.Type, "power",
            JsonSerializer.SerializeToElement(new { previous = gone.ToString(), created = created.ToString(), source = BuiltInPowerSchemes.Ultimate.ToString() }));

        await NewHandler().UndoAsync(recorded, ctx, Ct);

        Assert.Equal(BuiltInPowerSchemes.Balanced, power.Active);
        Assert.Equal([created], power.Deleted);
        Assert.Contains(sink.Events, e => e.Level == Serilog.Events.LogEventLevel.Warning);
    }

    [Fact]
    public async Task Apply_reuses_existing_created_scheme_after_switching_away()
    {
        var power = new FakePowerSchemes();
        var ctx = TestServices.Context(TestServices.Create(power: power));
        var action = new PowerSchemeAction("ultimate");
        var first = await NewHandler().ApplyAsync(action, await NewHandler().CaptureAsync(action, ctx, Ct), ctx, Ct);
        var created = Guid.Parse(first.Previous.GetProperty("created").GetString()!);
        await power.SetActiveAsync(BuiltInPowerSchemes.Balanced, Ct);
        var handler = NewHandler(created, BuiltInPowerSchemes.Ultimate);

        Assert.False(await handler.IsAppliedAsync(action, ctx, Ct));
        var second = await handler.ApplyAsync(action, await handler.CaptureAsync(action, ctx, Ct), ctx, Ct);

        Assert.Equal(created, power.Active);
        Assert.Equal([BuiltInPowerSchemes.Balanced, created], power.Schemes);
        Assert.Equal(created.ToString(), second.Previous.GetProperty("created").GetString());
    }

    [Fact]
    public async Task Undo_deletes_every_created_scheme_in_a_merged_record()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var power = new FakePowerSchemes { Active = b };
        power.Schemes.AddRange([a, b]);
        var ctx = TestServices.Context(TestServices.Create(power: power));
        JournalAction Record(Guid previous, Guid created) => new(PowerSchemeHandler.Type, "power",
            JsonSerializer.SerializeToElement(new { previous = previous.ToString(), created = created.ToString(), source = BuiltInPowerSchemes.Ultimate.ToString() }));

        var merged = PowerSchemeHandler.MergeForUndo([Record(BuiltInPowerSchemes.Balanced, a), Record(BuiltInPowerSchemes.Balanced, b)]);
        await NewHandler().UndoAsync(merged, ctx, Ct);

        Assert.Equal(BuiltInPowerSchemes.Balanced, power.Active);
        Assert.Equal([a, b], power.Deleted.Order());
        Assert.Equal([BuiltInPowerSchemes.Balanced], power.Schemes);
    }

    [Fact]
    public async Task Applied_when_active_is_created_scheme()
    {
        var created = Guid.NewGuid();
        var power = new FakePowerSchemes { Active = created };
        power.Schemes.Add(created);
        var ctx = TestServices.Context(TestServices.Create(power: power));
        var action = new PowerSchemeAction("ultimate");

        Assert.True(await NewHandler(created).IsAppliedAsync(action, ctx, Ct));
        Assert.False(await NewHandler().IsAppliedAsync(action, ctx, Ct));

        power.Active = BuiltInPowerSchemes.Ultimate;
        Assert.True(await NewHandler().IsAppliedAsync(action, ctx, Ct));
    }

    [Fact]
    public async Task Apply_failure_after_duplicate_deletes_created_scheme()
    {
        var power = new ThrowingPowerSchemes();
        var ctx = TestServices.Context(TestServices.Create(power: power));
        var handler = NewHandler();
        var action = new PowerSchemeAction("ultimate");
        var captured = await handler.CaptureAsync(action, ctx, Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ApplyAsync(action, captured, ctx, Ct));

        Assert.Single(power.Deleted);
        Assert.Equal([BuiltInPowerSchemes.Balanced], power.Schemes);
    }

    private sealed class CollectingSink : Serilog.Core.ILogEventSink
    {
        public List<Serilog.Events.LogEvent> Events { get; } = [];

        public void Emit(Serilog.Events.LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed class ThrowingPowerSchemes : FakePowerSchemes
    {
        public override Task SetActiveAsync(Guid scheme, CancellationToken ct) =>
            throw new InvalidOperationException("powercfg failed");
    }
}
