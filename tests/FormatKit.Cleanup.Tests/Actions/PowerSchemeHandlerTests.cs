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

    private sealed class ThrowingPowerSchemes : FakePowerSchemes
    {
        public override Task SetActiveAsync(Guid scheme, CancellationToken ct) =>
            throw new InvalidOperationException("powercfg failed");
    }
}
