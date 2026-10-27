using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Cleanup.Localization;
using FormatKit.Cleanup.Steps;
using FormatKit.Core;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using static FormatKit.Cleanup.Tests.TestItems;

namespace FormatKit.Cleanup.Tests.Steps;

public class CleanupCategoryStepTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly InMemoryFileSystem _files = new();
    private readonly FakeRegistry _registry = new();
    private readonly FakeHardwareInfo _hardware = new(FakeHardwareInfo.Sample());
    private readonly FakeUserSession _session = new();
    private readonly JournalStore _journal;
    private readonly StepContext _ctx;

    public CleanupCategoryStepTests()
    {
        _journal = new JournalStore(_files, new AppPaths(@"C:\FormatKitTest"));
        _ctx = TestServices.Context(TestServices.Create(files: _files, registry: _registry, hardware: _hardware, session: _session));
    }

    private CleanupCategoryStep Step(CleanupItem[] items, params string[] included) =>
        new(items[0].Category, items, new HashSet<string>(included),
            new ItemApplier(new ActionHandlers(() => new HashSet<Guid>()), _journal));

    private static string Message(CleanupItem item, string status) => $"{item.LocalizedText().Title}: {status}";

    [Fact]
    public void Exposes_id_title_and_flags()
    {
        var step = Step([Item("performance.a")]);

        Assert.Equal("cleanup.performance", step.Id);
        Assert.Equal(Strings.Get("Step_Title_Performance"), step.Title);
        Assert.True(step.RequiresAdmin);
        Assert.False(step.IsCritical);
    }

    [Fact]
    public async Task Laptop_sensitive_item_skipped_on_battery()
    {
        _hardware.HasBattery = true;
        var item = Item("performance.power", laptopSensitive: true);

        var result = await Step([item]).ExecuteAsync(_ctx, Ct);

        Assert.Equal(StepOutcome.Skipped, result.Outcome);
        Assert.Equal([Message(item, Strings.Get("Skip_Battery"))], result.Messages);
        Assert.Empty(_registry.Values);
    }

    [Fact]
    public async Task Included_laptop_item_applies_on_battery()
    {
        _hardware.HasBattery = true;
        var item = Item("performance.power", laptopSensitive: true);

        var result = await Step([item], "performance.power").ExecuteAsync(_ctx, Ct);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Equal([Message(item, Strings.Get("Status_Applied"))], result.Messages);
    }

    [Fact]
    public async Task User_items_skipped_for_other_admin_machine_items_apply()
    {
        _session.IsConsoleUser = false;
        var user = Item("privacy.user", scope: ItemScope.User, actions: [Dword(@"HKCU\Software\Test", "U", 0)]);
        var machine = Item("privacy.machine");

        var result = await Step([user, machine]).ExecuteAsync(_ctx, Ct);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Equal(
            [Message(user, Strings.Get("Skip_OtherUser")), Message(machine, Strings.Get("Status_Applied"))],
            result.Messages);
        Assert.Null(_registry.Read(@"HKCU\Software\Test", "U"));
    }

    [Fact]
    public async Task Reboot_item_yields_RebootRequired()
    {
        _registry.Set(@"HKLM\Software\Test", "performance.done", RegistryKind.Dword, "0");
        var items = new[] { Item("performance.done", requiresReboot: true), Item("performance.reboot", requiresReboot: true) };

        var result = await Step(items).ExecuteAsync(_ctx, Ct);

        Assert.Equal(StepOutcome.RebootRequired, result.Outcome);
        Assert.Equal(
            [Message(items[0], Strings.Get("Status_AlreadyInPlace")), Message(items[1], Strings.Get("Status_Applied"))],
            result.Messages);
    }

    [Fact]
    public async Task Nothing_applied_yields_Skipped()
    {
        _registry.Set(@"HKLM\Software\Test", "apps.done", RegistryKind.Dword, "0");

        var result = await Step([Item("apps.done", requiresReboot: true)]).ExecuteAsync(_ctx, Ct);

        Assert.Equal(StepOutcome.Skipped, result.Outcome);
    }

    [Fact]
    public async Task Any_failure_yields_Failed_with_next_action()
    {
        var bad = Item("apps.bad", actions: [new NamedAction("unknownHandler")]);
        var good = Item("apps.good", requiresReboot: true);

        var result = await Step([bad, good]).ExecuteAsync(_ctx, Ct);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(Strings.Get("NextAction_SeeLog"), result.NextAction);
        Assert.Equal(2, result.Messages.Count);
        Assert.StartsWith($"{bad.LocalizedText().Title}: ", result.Messages[0]);
        Assert.Equal(Message(good, Strings.Get("Status_Applied")), result.Messages[1]);
    }

    [Fact]
    public async Task Corrupt_journal_fails_step_with_path()
    {
        _files.Seed(_journal.Path, "{ not json");

        var result = await Step([Item("apps.a")]).ExecuteAsync(_ctx, Ct);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(Strings.Format("NextAction_JournalCorrupt", _journal.Path), result.NextAction);
        Assert.Contains(_journal.Path, result.NextAction);
        Assert.Empty(_registry.Values);
    }

    [Fact]
    public async Task Check_true_when_everything_applied()
    {
        _hardware.HasBattery = true;
        var applied = Item("performance.applied");
        var laptop = Item("performance.laptop", laptopSensitive: true);
        var step = Step([applied, laptop]);

        Assert.False(await step.CheckAsync(_ctx, Ct));

        _registry.Set(@"HKLM\Software\Test", "performance.applied", RegistryKind.Dword, "0");
        Assert.True(await step.CheckAsync(_ctx, Ct));
    }
}
