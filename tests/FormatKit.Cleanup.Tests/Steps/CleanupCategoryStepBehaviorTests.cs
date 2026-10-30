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

/// <summary>Explorer restart and the noPrinter condition (spec 5.1, 8).</summary>
public class CleanupCategoryStepBehaviorTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string ExplorerLine = "\"explorer.exe\",\"1234\",\"Console\",\"1\",\"80,000 K\"";
    private const string NoTasks = "INFO: No tasks are running which match the specified criteria.";

    private readonly InMemoryFileSystem _files = new();
    private readonly FakeRegistry _registry = new();
    private readonly FakeProcessRunner _processes = new();
    private readonly FakePrinters _printers = new();
    private readonly List<TimeSpan> _delays = [];
    private readonly StepContext _ctx;
    private readonly JournalStore _journal;

    public CleanupCategoryStepBehaviorTests()
    {
        _journal = new JournalStore(_files, new AppPaths(@"C:\FormatKitTest"));
        _ctx = TestServices.Context(TestServices.Create(files: _files, registry: _registry, processes: _processes, printers: _printers));
    }

    private CleanupCategoryStep Step(params CleanupItem[] items) =>
        new(items[0].Category, items, new HashSet<string>(),
            new ItemApplier(new ActionHandlers(() => new Dictionary<Guid, Guid?>()), _journal),
            (d, _) => { _delays.Add(d); return Task.CompletedTask; });

    private static CleanupItem Restarting(string id) => Item(id) with { RestartsExplorer = true };

    private static ProcessResult Ok(string stdout = "") => new(0, stdout, "", TimeSpan.Zero, false);

    private static string Message(CleanupItem item, string status) => $"{item.LocalizedText().Title}: {status}";

    [Fact]
    public async Task Two_restarting_items_cause_one_taskkill_after_the_last_item_and_no_explorer_start()
    {
        _processes.Enqueue("taskkill.exe", Ok());
        _processes.Enqueue("tasklist.exe", Ok(ExplorerLine));
        var order = new List<string>();
        _processes.OnRun = spec => order.Add(spec.FileName);
        var a = Restarting("interface.a");
        var b = Restarting("interface.b");

        var result = await Step(a, b).ExecuteAsync(_ctx, Ct);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Equal(["taskkill.exe", "tasklist.exe"], order);
        Assert.Equal(["/f", "/im", "explorer.exe"], _processes.Calls[0].Arguments);
        Assert.Equal(["/fi", "imagename eq explorer.exe", "/fo", "csv", "/nh"], _processes.Calls[1].Arguments);
        Assert.Equal(2, result.Messages.Count);
        Assert.Null(result.NextAction);
    }

    [Fact]
    public async Task Explorer_that_reappears_after_some_polls_gives_no_warning()
    {
        _processes.Enqueue("taskkill.exe", Ok());
        _processes.Enqueue("tasklist.exe", Ok(NoTasks));
        _processes.Enqueue("tasklist.exe", Ok(NoTasks));
        _processes.Enqueue("tasklist.exe", Ok(ExplorerLine));

        var result = await Step(Restarting("interface.a")).ExecuteAsync(_ctx, Ct);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Single(result.Messages);
        Assert.Equal(2, _delays.Count);
        Assert.Null(result.NextAction);
    }

    [Fact]
    public async Task Explorer_that_never_reappears_still_succeeds_with_the_warning()
    {
        _processes.Enqueue("taskkill.exe", Ok());
        for (var i = 0; i < 40; i++)
            _processes.Enqueue("tasklist.exe", Ok(NoTasks));

        var result = await Step(Restarting("interface.a")).ExecuteAsync(_ctx, Ct);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Equal(Strings.Get("Warning_ExplorerRestartFailed"), result.Messages[^1]);
        Assert.Equal(Strings.Get("NextAction_SignOutAndIn"), result.NextAction);
        Assert.True(_delays.Aggregate(TimeSpan.Zero, (x, y) => x + y) <= TimeSpan.FromSeconds(15));
        Assert.DoesNotContain(_processes.Calls, c => c.FileName == "cmd.exe" || c.FileName.Contains("explorer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Failing_taskkill_gives_the_warning()
    {
        _processes.Enqueue("taskkill.exe", new ProcessResult(1, "", "denied", TimeSpan.Zero, false));

        var result = await Step(Restarting("interface.a")).ExecuteAsync(_ctx, Ct);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Equal(Strings.Get("Warning_ExplorerRestartFailed"), result.Messages[^1]);
    }

    [Fact]
    public async Task No_applied_restarting_item_means_no_restart()
    {
        _registry.Set(@"HKLM\Software\Test", "interface.done", RegistryKind.Dword, "0");

        var result = await Step(Restarting("interface.done"), Item("interface.plain")).ExecuteAsync(_ctx, Ct);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Empty(_processes.Calls);
    }

    [Fact]
    public async Task Skipped_restarting_item_means_no_restart()
    {
        _printers.Names.Add("HP LaserJet");
        var item = Restarting("interface.a") with { Condition = ItemCondition.NoPrinter };

        var result = await Step(item).ExecuteAsync(_ctx, Ct);

        Assert.Equal(StepOutcome.Skipped, result.Outcome);
        Assert.Empty(_processes.Calls);
    }

    [Fact]
    public async Task NoPrinter_item_is_skipped_when_a_real_printer_is_listed()
    {
        _printers.Names.AddRange(["Microsoft Print to PDF", "HP LaserJet"]);
        var item = Item("performance.spooler") with { Condition = ItemCondition.NoPrinter };

        var step = Step(item);
        var result = await step.ExecuteAsync(_ctx, Ct);

        Assert.False(await step.CheckAsync(_ctx, Ct));
        Assert.Equal(StepOutcome.Skipped, result.Outcome);
        Assert.Equal([Message(item, Strings.Get("Skip_PrinterInstalled"))], result.Messages);
        Assert.Empty(_registry.Values);
    }

    [Fact]
    public async Task NoPrinter_item_applies_when_only_virtual_printers_are_listed()
    {
        _printers.Names.AddRange(["Microsoft Print to PDF", "Microsoft XPS Document Writer", "OneNote (Desktop)", "Fax"]);
        var item = Item("performance.spooler") with { Condition = ItemCondition.NoPrinter };

        var result = await Step(item).ExecuteAsync(_ctx, Ct);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Equal([Message(item, Strings.Get("Status_Applied"))], result.Messages);
    }

    [Fact]
    public async Task NoPrinter_item_applies_when_no_printer_exists()
    {
        var item = Item("performance.spooler") with { Condition = ItemCondition.NoPrinter };

        var result = await Step(item).ExecuteAsync(_ctx, Ct);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
    }

    [Fact]
    public async Task Failed_printer_listing_skips_the_item_as_if_a_printer_were_installed()
    {
        _printers.Failure = new InvalidOperationException("Get-Printer failed");
        var item = Item("performance.spooler") with { Condition = ItemCondition.NoPrinter };

        var result = await Step(item).ExecuteAsync(_ctx, Ct);

        Assert.Equal(StepOutcome.Skipped, result.Outcome);
        Assert.Equal([Message(item, Strings.Get("Skip_PrinterInstalled"))], result.Messages);
    }

    [Fact]
    public async Task Printers_are_not_listed_when_no_item_has_the_condition()
    {
        await Step(Item("performance.a")).ExecuteAsync(_ctx, Ct);

        Assert.Equal(0, _printers.Calls);
    }
}
