using System.Text.Json;
using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Journal;
using FormatKit.Cleanup.Catalog;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Cleanup.Tests.Actions;

public class HibernateHandlerTests
{
    private const string Power = @"HKLM\SYSTEM\CurrentControlSet\Control\Power";
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly NamedAction Action = new("hibernate");

    /// <summary>The fake powercfg: /hibernate off|on sets HibernateEnabled (unless <paramref name="takesEffect"/> is false).</summary>
    private static (FakeRegistry Registry, FakeProcessRunner Runner, StepContext Ctx) Setup(string? enabled, string? enabledDefault = "1", bool takesEffect = true)
    {
        var registry = new FakeRegistry();
        if (enabled is not null) registry.Set(Power, "HibernateEnabled", RegistryKind.Dword, enabled);
        if (enabledDefault is not null) registry.Set(Power, "HibernateEnabledDefault", RegistryKind.Dword, enabledDefault);
        var runner = new FakeProcessRunner
        {
            OnRun = spec =>
            {
                if (takesEffect)
                    registry.Set(Power, "HibernateEnabled", RegistryKind.Dword, spec.Arguments[1] == "off" ? "0" : "1");
            },
        };
        runner.Enqueue("powercfg.exe", new ProcessResult(0, "", "", TimeSpan.Zero, false));
        return (registry, runner, TestServices.Context(TestServices.Create(processes: runner, registry: registry)));
    }

    [Theory]
    [InlineData("0", "1", true)]
    [InlineData("1", "0", false)]
    [InlineData(null, "1", false)]
    [InlineData(null, "0", true)]
    public async Task Check_reads_the_value_and_falls_back_to_the_default(string? enabled, string? def, bool expected)
    {
        var (_, _, ctx) = Setup(enabled, def);
        Assert.Equal(expected, await new HibernateHandler().IsAppliedAsync(Action, ctx, Ct));
    }

    [Fact]
    public async Task Apply_runs_powercfg_off_and_verifies()
    {
        var (registry, runner, ctx) = Setup(null);
        var handler = new HibernateHandler();

        var captured = await handler.CaptureAsync(Action, ctx, Ct);
        Assert.Equal("hibernate", captured.Target);
        await handler.ApplyAsync(Action, captured, ctx, Ct);

        Assert.Equal("powercfg.exe", runner.Calls.Single().FileName);
        Assert.Equal(["/hibernate", "off"], runner.Calls.Single().Arguments);
        Assert.Equal(new RegistryValue(RegistryKind.Dword, "0"), registry.Read(Power, "HibernateEnabled"));
    }

    [Fact]
    public async Task Apply_throws_when_the_state_does_not_change()
    {
        var (_, _, ctx) = Setup("1", takesEffect: false);
        var handler = new HibernateHandler();
        var captured = await handler.CaptureAsync(Action, ctx, Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ApplyAsync(Action, captured, ctx, Ct));
    }

    [Fact]
    public async Task Apply_throws_when_powercfg_fails()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("powercfg.exe", new ProcessResult(1, "", "no", TimeSpan.Zero, false));
        var ctx = TestServices.Context(TestServices.Create(processes: runner, registry: new FakeRegistry()));
        var handler = new HibernateHandler();
        var captured = await handler.CaptureAsync(Action, ctx, Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ApplyAsync(Action, captured, ctx, Ct));
    }

    [Fact]
    public async Task Undo_runs_powercfg_on_and_restores_the_value_when_it_was_one()
    {
        var (registry, runner, ctx) = Setup("1");
        var handler = new HibernateHandler();
        var recorded = await handler.ApplyAsync(Action, await handler.CaptureAsync(Action, ctx, Ct), ctx, Ct);
        runner.OnRun = null;
        runner.Enqueue("powercfg.exe", new ProcessResult(0, "", "", TimeSpan.Zero, false));

        await handler.UndoAsync(recorded, ctx, Ct);

        Assert.Equal(["/hibernate", "on"], runner.Calls[1].Arguments);
        Assert.Equal(new RegistryValue(RegistryKind.Dword, "1"), registry.Read(Power, "HibernateEnabled"));
    }

    [Fact]
    public async Task Undo_turns_hibernation_on_then_deletes_the_value_when_it_was_absent_and_the_default_was_on()
    {
        var (registry, runner, ctx) = Setup(null, "1");
        var handler = new HibernateHandler();
        var recorded = await handler.ApplyAsync(Action, await handler.CaptureAsync(Action, ctx, Ct), ctx, Ct);
        runner.Enqueue("powercfg.exe", new ProcessResult(0, "", "", TimeSpan.Zero, false));

        await handler.UndoAsync(recorded, ctx, Ct);

        Assert.Equal(["/hibernate", "on"], runner.Calls[1].Arguments);
        Assert.Null(registry.Read(Power, "HibernateEnabled"));
    }

    [Fact]
    public async Task Undo_logs_a_powercfg_failure_and_still_deletes_the_value()
    {
        var (registry, runner, ctx) = Setup(null, "1");
        var handler = new HibernateHandler();
        var recorded = await handler.ApplyAsync(Action, await handler.CaptureAsync(Action, ctx, Ct), ctx, Ct);
        runner.OnRun = null;
        runner.Enqueue("powercfg.exe", new ProcessResult(1, "", "The system firmware does not support hibernation.", TimeSpan.Zero, false));

        await handler.UndoAsync(recorded, ctx, Ct);

        Assert.Equal(2, runner.Calls.Count);
        Assert.Null(registry.Read(Power, "HibernateEnabled"));
    }

    [Fact]
    public async Task Undo_of_a_journal_without_the_effective_state_reads_the_default_now()
    {
        var (registry, runner, ctx) = Setup("0", "1");
        var recorded = new JournalAction("hibernate", "hibernate",
            JsonSerializer.SerializeToElement(new { enabled = new { absent = true } }));

        await new HibernateHandler().UndoAsync(recorded, ctx, Ct);

        Assert.Equal(["/hibernate", "on"], runner.Calls.Single().Arguments);
        Assert.Null(registry.Read(Power, "HibernateEnabled"));
    }

    [Fact]
    public async Task Undo_does_nothing_when_it_was_zero()
    {
        var (_, runner, ctx) = Setup("0");
        var handler = new HibernateHandler();
        var recorded = await handler.CaptureAsync(Action, ctx, Ct);

        await handler.UndoAsync(recorded, ctx, Ct);

        Assert.Empty(runner.Calls);
    }
}
