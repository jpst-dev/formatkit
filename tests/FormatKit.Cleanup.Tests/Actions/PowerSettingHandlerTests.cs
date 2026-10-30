using System.Text.Json;
using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Cleanup.Tests.Actions;

public class PowerSettingHandlerTests
{
    private const string Display = "7516b95f-f776-4464-8c53-06167f40cc99";
    private const string VideoIdle = "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e";
    private static readonly Guid Balanced = BuiltInPowerSchemes.Balanced;
    private static readonly PowerSettingAction Monitor15 = new(Display, VideoIdle, 900, null);
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static (PowerSettingHandler Handler, FakePowerSchemes Power, StepContext Ctx) Create(
        int ac = 300, int dc = 180, bool absent = false, Guid? active = null)
    {
        var power = new FakePowerSchemes();
        if (active is { } a)
        {
            power.Schemes.Add(a);
            power.Active = a;
        }

        if (!absent)
            power.SetStored(power.Active, Display, VideoIdle, ac, dc);
        return (new PowerSettingHandler(), power, TestServices.Context(TestServices.Create(power: power)));
    }

    private static JournalAction Recorded(Guid scheme) =>
        new(PowerSettingHandler.Type, $"power:{Display}/{VideoIdle}",
            JsonSerializer.SerializeToElement(new { scheme = scheme.ToString(), ac = 300, dc = 180 }));

    [Fact]
    public async Task Target_values_on_the_active_scheme_are_applied()
    {
        var (handler, _, ctx) = Create(900, 180);
        Assert.True(await handler.IsAppliedAsync(Monitor15, ctx, Ct));
    }

    [Fact]
    public async Task Different_ac_is_not_applied()
    {
        var (handler, _, ctx) = Create(300, 180);
        Assert.False(await handler.IsAppliedAsync(Monitor15, ctx, Ct));
    }

    [Fact]
    public async Task Dc_is_compared_only_when_the_action_sets_it()
    {
        var (handler, _, ctx) = Create(900, 180);
        Assert.False(await handler.IsAppliedAsync(Monitor15 with { Dc = 600 }, ctx, Ct));
        Assert.True(await handler.IsAppliedAsync(Monitor15 with { Dc = 180 }, ctx, Ct));
    }

    [Fact]
    public async Task Absent_setting_counts_as_applied_but_cannot_be_captured()
    {
        var (handler, power, ctx) = Create(absent: true);

        Assert.True(await handler.IsAppliedAsync(Monitor15, ctx, Ct));
        await Assert.ThrowsAsync<TargetNotPresentException>(() => handler.CaptureAsync(Monitor15, ctx, Ct));
        Assert.Empty(power.Writes);
    }

    [Fact]
    public async Task Capture_records_the_active_scheme_and_previous_indexes()
    {
        var (handler, power, ctx) = Create();

        var captured = await handler.CaptureAsync(Monitor15, ctx, Ct);

        Assert.Equal(PowerSettingHandler.Type, captured.Type);
        Assert.Equal($"power:{Display}/{VideoIdle}", captured.Target);
        Assert.Equal(Balanced.ToString(), captured.Previous.GetProperty("scheme").GetString());
        Assert.Equal(300, captured.Previous.GetProperty("ac").GetInt32());
        Assert.Equal(180, captured.Previous.GetProperty("dc").GetInt32());
        Assert.Empty(power.Writes);
    }

    [Fact]
    public async Task Apply_writes_the_active_scheme_and_leaves_dc_alone_when_unset()
    {
        var (handler, power, ctx) = Create();

        var captured = await handler.CaptureAsync(Monitor15, ctx, Ct);
        var recorded = await handler.ApplyAsync(Monitor15, captured, ctx, Ct);

        var write = Assert.Single(power.Writes);
        Assert.Equal((Balanced, 900, (int?)null), (write.Scheme, write.Ac, write.Dc));
        Assert.Equal((900, 180), await power.GetValueAsync(Balanced, Display, VideoIdle, Ct));
        Assert.Equal(captured, recorded);
    }

    [Fact]
    public async Task Apply_throws_when_the_value_does_not_stick()
    {
        var (handler, power, ctx) = Create();
        power.IgnoreWrites = true;

        var captured = await handler.CaptureAsync(Monitor15, ctx, Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ApplyAsync(Monitor15, captured, ctx, Ct));
    }

    [Fact]
    public async Task Undo_writes_the_journaled_scheme()
    {
        var other = Guid.NewGuid();
        var (handler, power, ctx) = Create(active: other);
        power.SetStored(Balanced, Display, VideoIdle, 900, 600);

        await handler.UndoAsync(Recorded(Balanced), ctx, Ct);

        var write = Assert.Single(power.Writes);
        Assert.Equal((Balanced, 300, (int?)180), (write.Scheme, write.Ac, write.Dc));
        Assert.Equal((300, 180), await power.GetValueAsync(Balanced, Display, VideoIdle, Ct));
    }

    [Fact]
    public async Task Undo_when_the_scheme_no_longer_exists_logs_and_succeeds()
    {
        var (handler, power, ctx) = Create();

        await handler.UndoAsync(Recorded(Guid.NewGuid()), ctx, Ct);

        Assert.Empty(power.Writes);
    }
}
