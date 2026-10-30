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
        Assert.Equal(new[] { a, b }.Order(), power.Deleted.Order());
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

    private static readonly PowerSchemeAction FormatKit = new("formatkit");

    private static PowerSchemeHandler FormatKitHandler(params Guid[] formatKitSchemes) =>
        new(() => formatKitSchemes.ToDictionary(g => g, _ => (Guid?)BuiltInPowerSchemes.HighPerformance), () => formatKitSchemes.ToHashSet());

    /// <summary>Makes the fake report every FormatKit setting on the scheme the next duplicate will create.</summary>
    private static void SeedNext(FakePowerSchemes power, Func<(string Subgroup, string Setting, int Value), bool>? only = null)
    {
        foreach (var s in PowerSchemeHandler.FormatKitSettings.Where(s => only?.Invoke(s) ?? true))
            power.SetStored(power.NextCreated, s.Subgroup, s.Setting, 1, 1);
    }

    [Fact]
    public void FormatKit_settings_are_the_six_spec_values()
    {
        Assert.Equal(
            [
                ("54533251-82be-4824-96c1-47b60b740d00", "0cc5b647-c1df-4637-891a-dec35c318583", 100),
                ("54533251-82be-4824-96c1-47b60b740d00", "36687f9e-e3a5-4dbf-b1dc-15eb381c6863", 0),
                ("54533251-82be-4824-96c1-47b60b740d00", "be337238-0d82-4146-a960-4f3749d470c7", 2),
                ("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", 0),
                ("2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0),
                ("0012ee47-9041-4b5d-9b77-535fba8b1442", "6738e2c4-e8a5-4a42-b16a-e040e769756e", 0),
            ],
            PowerSchemeHandler.FormatKitSettings);
    }

    [Fact]
    public async Task FormatKit_apply_duplicates_high_performance_renames_writes_activates_and_journals()
    {
        var power = new FakePowerSchemes();
        SeedNext(power);
        var ctx = TestServices.Context(TestServices.Create(power: power));
        var handler = FormatKitHandler();

        var captured = await handler.CaptureAsync(FormatKit, ctx, Ct);
        var recorded = await handler.ApplyAsync(FormatKit, captured, ctx, Ct);

        var created = Guid.Parse(recorded.Previous.GetProperty("created").GetString()!);
        Assert.Equal(created, power.Active);
        Assert.Equal([BuiltInPowerSchemes.HighPerformance], power.Duplicated);
        Assert.Equal(created, Assert.Single(power.Renames).Scheme);
        Assert.Equal(PowerSchemeHandler.FormatKitSettings.Count, power.Writes.Count);
        Assert.All(power.Writes, w => { Assert.Equal(created, w.Scheme); Assert.Equal(w.Ac, w.Dc); });
        Assert.Equal(PowerSchemeHandler.FormatKitSettings.Select(s => s.Value), power.Writes.Select(w => w.Ac));
        Assert.Equal(BuiltInPowerSchemes.HighPerformance.ToString(), recorded.Previous.GetProperty("source").GetString());
        Assert.True(recorded.Previous.GetProperty("formatkit").GetBoolean());
        Assert.Equal(BuiltInPowerSchemes.Balanced.ToString(), recorded.Previous.GetProperty("previous").GetString());
        Assert.Equal(6, recorded.Previous.GetProperty("settings").GetArrayLength());
    }

    [Fact]
    public async Task FormatKit_apply_skips_a_setting_absent_on_the_machine_and_logs_it()
    {
        var power = new FakePowerSchemes();
        var sink = new CollectingSink();
        var ctx = new Core.Steps.StepContext(TestServices.Create(power: power), new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger());
        var absent = PowerSchemeHandler.FormatKitSettings[1];
        SeedNext(power, s => s != absent);
        var handler = FormatKitHandler();

        var recorded = await handler.ApplyAsync(FormatKit, await handler.CaptureAsync(FormatKit, ctx, Ct), ctx, Ct);

        Assert.Equal(5, power.Writes.Count);
        Assert.DoesNotContain(power.Writes, w => w.Setting == absent.Setting);
        Assert.Equal(5, recorded.Previous.GetProperty("settings").GetArrayLength());
        Assert.Contains(sink.Events, e => e.RenderMessage().Contains(absent.Setting));

        // The check ignores the absent setting.
        Assert.True(await FormatKitHandler(power.Active).IsAppliedAsync(FormatKit, ctx, Ct));
    }

    [Fact]
    public async Task FormatKit_second_apply_reuses_the_created_scheme()
    {
        var power = new FakePowerSchemes();
        var ctx = TestServices.Context(TestServices.Create(power: power));
        var created = Guid.NewGuid();
        power.Schemes.Add(created);
        foreach (var s in PowerSchemeHandler.FormatKitSettings)
            power.SetStored(created, s.Subgroup, s.Setting, 7, 7);
        var handler = FormatKitHandler(created);

        var recorded = await handler.ApplyAsync(FormatKit, await handler.CaptureAsync(FormatKit, ctx, Ct), ctx, Ct);

        Assert.Empty(power.Duplicated);
        Assert.Empty(power.Renames);
        Assert.Equal(created, power.Active);
        Assert.Equal(created.ToString(), recorded.Previous.GetProperty("created").GetString());
        Assert.Equal(6, power.Writes.Count);
    }

    [Fact]
    public async Task FormatKit_check_is_false_when_a_value_drifted_and_true_when_all_match()
    {
        var created = Guid.NewGuid();
        var power = new FakePowerSchemes { Active = created };
        power.Schemes.Add(created);
        foreach (var (subgroup, setting, value) in PowerSchemeHandler.FormatKitSettings)
            power.SetStored(created, subgroup, setting, value, value);
        var ctx = TestServices.Context(TestServices.Create(power: power));
        var handler = FormatKitHandler(created);

        Assert.True(await handler.IsAppliedAsync(FormatKit, ctx, Ct));

        var drifted = PowerSchemeHandler.FormatKitSettings[2];
        power.SetStored(created, drifted.Subgroup, drifted.Setting, drifted.Value, 0);
        Assert.False(await handler.IsAppliedAsync(FormatKit, ctx, Ct));

        power.SetStored(created, drifted.Subgroup, drifted.Setting, 1, drifted.Value);
        Assert.False(await handler.IsAppliedAsync(FormatKit, ctx, Ct));
    }

    [Fact]
    public async Task FormatKit_check_is_false_when_the_active_scheme_is_not_the_formatkit_one()
    {
        var created = Guid.NewGuid();
        var power = new FakePowerSchemes();
        power.Schemes.Add(created);
        foreach (var (subgroup, setting, value) in PowerSchemeHandler.FormatKitSettings)
            power.SetStored(power.Active, subgroup, setting, value, value);
        var ctx = TestServices.Context(TestServices.Create(power: power));

        Assert.False(await FormatKitHandler(created).IsAppliedAsync(FormatKit, ctx, Ct));
        Assert.False(await FormatKitHandler().IsAppliedAsync(FormatKit, ctx, Ct));
    }

    [Fact]
    public async Task FormatKit_undo_reactivates_previous_and_deletes_the_scheme()
    {
        var power = new FakePowerSchemes();
        SeedNext(power);
        var ctx = TestServices.Context(TestServices.Create(power: power));
        var handler = FormatKitHandler();
        var recorded = await handler.ApplyAsync(FormatKit, await handler.CaptureAsync(FormatKit, ctx, Ct), ctx, Ct);
        var created = power.Active;

        await handler.UndoAsync(recorded, ctx, Ct);

        Assert.Equal(BuiltInPowerSchemes.Balanced, power.Active);
        Assert.Equal([created], power.Deleted);
    }

    [Fact]
    public async Task FormatKit_apply_failure_deletes_the_created_scheme()
    {
        var power = new ThrowingPowerSchemes();
        SeedNext(power);
        var ctx = TestServices.Context(TestServices.Create(power: power));
        var handler = FormatKitHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await handler.ApplyAsync(FormatKit, await handler.CaptureAsync(FormatKit, ctx, Ct), ctx, Ct));

        Assert.Single(power.Deleted);
        Assert.Equal([BuiltInPowerSchemes.Balanced], power.Schemes);
    }

    [Fact]
    public async Task FormatKit_flagged_scheme_is_never_reused_for_the_plain_high_performance_item()
    {
        var created = Guid.NewGuid();
        var power = new FakePowerSchemes();
        power.Schemes.Add(created);
        var ctx = TestServices.Context(TestServices.Create(power: power));
        var handler = FormatKitHandler(created);
        var action = new PowerSchemeAction("highPerformance");

        var recorded = await handler.ApplyAsync(action, await handler.CaptureAsync(action, ctx, Ct), ctx, Ct);

        Assert.NotEqual(created, power.Active);
        Assert.Equal(power.Active.ToString(), recorded.Previous.GetProperty("created").GetString());
        Assert.False(recorded.Previous.TryGetProperty("formatkit", out _));
    }

    [Fact]
    public void FormatKit_schemes_come_from_flagged_unreverted_entries()
    {
        static JournalAction Power(Guid created, bool flag) => new(PowerSchemeHandler.Type, "power",
            JsonSerializer.SerializeToElement(new { previous = Guid.NewGuid().ToString(), created = created.ToString(), source = BuiltInPowerSchemes.HighPerformance.ToString(), formatkit = flag ? (bool?)true : null }));
        var flagged = Guid.NewGuid();
        var plain = Guid.NewGuid();
        var reverted = Guid.NewGuid();
        var journal = new CleanupJournal(1,
        [
            new JournalEntry("performance.a", DateTimeOffset.Now, [Power(flagged, true), Power(plain, false)], null),
            new JournalEntry("performance.b", DateTimeOffset.Now, [Power(reverted, true)], DateTimeOffset.Now),
        ]);

        Assert.Equal([flagged], PowerSchemeHandler.FormatKitSchemes(journal));
        Assert.Empty(PowerSchemeHandler.FormatKitSchemes(null));
    }

    [Fact]
    public async Task Journal_from_before_the_formatkit_marker_still_parses_and_undoes()
    {
        const string oldJson = """
            {"schemaVersion":1,"entries":[{"itemId":"performance.ultimate","appliedAt":"2026-10-01T10:00:00+00:00","actions":[
              {"type":"powerScheme","target":"power","previous":{"previous":"381b4222-f694-41f0-9685-ff5bb260df2e","created":"11111111-2222-3333-4444-555555555555","source":"e9a42b02-d5df-448d-aa00-03f14749eb61"}}
            ],"revertedAt":null}]}
            """;
        var journal = JsonSerializer.Deserialize<CleanupJournal>(oldJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var created = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Assert.Equal([created], PowerSchemeHandler.CreatedSchemes(journal).Keys);
        Assert.Empty(PowerSchemeHandler.FormatKitSchemes(journal));

        var power = new FakePowerSchemes { Active = created };
        power.Schemes.Add(created);
        var ctx = TestServices.Context(TestServices.Create(power: power));
        await FormatKitHandler().UndoAsync(journal.Entries[0].Actions[0], ctx, Ct);

        Assert.Equal(BuiltInPowerSchemes.Balanced, power.Active);
        Assert.Equal([created], power.Deleted);
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
