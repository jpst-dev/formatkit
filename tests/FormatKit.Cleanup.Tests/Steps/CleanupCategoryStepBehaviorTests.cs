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
    private readonly FakeUserContextRunner _userContext = new();
    private readonly FakeUserSession _session = new() { ConsoleSessionId = 1 };
    private readonly List<TimeSpan> _delays = [];
    private readonly StepContext _ctx;
    private readonly JournalStore _journal;

    public CleanupCategoryStepBehaviorTests()
    {
        _journal = new JournalStore(_files, new AppPaths(@"C:\FormatKitTest"));
        _ctx = TestServices.Context(TestServices.Create(files: _files, registry: _registry, processes: _processes, printers: _printers, userContext: _userContext, session: _session));
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
        Assert.Equal(["/f", "/fi", "imagename eq explorer.exe", "/fi", "session eq 1"], _processes.Calls[0].Arguments);
        Assert.Equal(["/fi", "imagename eq explorer.exe", "/fi", "session eq 1", "/fo", "csv", "/nh"], _processes.Calls[1].Arguments);
        Assert.Equal(2, result.Messages.Count);
        Assert.Null(result.NextAction);
    }

    // Final review M4: an unfiltered taskkill also ended the shells of other users signed in through fast user switching.
    [Fact]
    public async Task Restart_kills_and_polls_only_the_console_session_explorer()
    {
        _session.ConsoleSessionId = 3;
        _processes.Enqueue("taskkill.exe", Ok());
        for (var i = 0; i < 3; i++)
            _processes.Enqueue("tasklist.exe", Ok(NoTasks));
        _processes.Enqueue("tasklist.exe", Ok(ExplorerLine));

        var result = await Step(Restarting("interface.a")).ExecuteAsync(_ctx, Ct);

        Assert.Null(result.NextAction);
        Assert.Equal(["/f", "/fi", "imagename eq explorer.exe", "/fi", "session eq 3"], _processes.Calls[0].Arguments);
        Assert.All(_processes.Calls.Where(c => c.FileName == "tasklist.exe"),
            c => Assert.Equal(["/fi", "imagename eq explorer.exe", "/fi", "session eq 3", "/fo", "csv", "/nh"], c.Arguments));
        Assert.DoesNotContain(_processes.Calls, c => c.Arguments.Contains("/im"));
    }

    [Fact]
    public async Task Unknown_console_session_kills_nothing_and_gives_the_warning()
    {
        _session.ConsoleSessionId = null;

        var result = await Step(Restarting("interface.a")).ExecuteAsync(_ctx, Ct);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Empty(_processes.Calls);
        Assert.Empty(_userContext.Calls);
        Assert.Equal(Strings.Get("Warning_ExplorerRestartFailed"), result.Messages[^1]);
        Assert.Equal(Strings.Get("NextAction_SignOutAndIn"), result.NextAction);
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
        Assert.Equal(3, _delays.Count);
        Assert.Null(result.NextAction);
    }

    [Fact]
    public async Task Explorer_that_reappears_by_itself_is_not_started()
    {
        _processes.Enqueue("taskkill.exe", Ok());
        _processes.Enqueue("tasklist.exe", Ok(NoTasks));
        _processes.Enqueue("tasklist.exe", Ok(NoTasks));
        _processes.Enqueue("tasklist.exe", Ok(ExplorerLine));

        await Step(Restarting("interface.a")).ExecuteAsync(_ctx, Ct);

        Assert.Empty(_userContext.Calls);
    }

    // VM run 2026-10-29: after taskkill Winlogon did not bring Explorer back, leaving the desktop without a taskbar.
    [Fact]
    public async Task Explorer_that_does_not_reappear_by_itself_is_started_unelevated_as_the_signed_in_user()
    {
        _processes.Enqueue("taskkill.exe", Ok());
        for (var i = 0; i < 3; i++)
            _processes.Enqueue("tasklist.exe", Ok(NoTasks));
        _processes.Enqueue("tasklist.exe", Ok(NoTasks));
        _processes.Enqueue("tasklist.exe", Ok(ExplorerLine));

        var result = await Step(Restarting("interface.a")).ExecuteAsync(_ctx, Ct);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Single(result.Messages);
        Assert.Null(result.NextAction);
        var start = Assert.Single(_userContext.Calls);
        Assert.Equal("cmd.exe", start.FileName);
        Assert.Equal(["/c", "start", "explorer.exe"], start.Arguments);
        Assert.Equal(5, _processes.Calls.Count(c => c.FileName == "tasklist.exe"));
        Assert.DoesNotContain(_processes.Calls, c => c.FileName is "explorer.exe" or "cmd.exe");
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
        Assert.Equal(18, _delays.Count);
        Assert.All(_delays, d => Assert.Equal(TimeSpan.FromSeconds(1), d));
        Assert.Equal(18, _processes.Calls.Count(c => c.FileName == "tasklist.exe"));
        Assert.Single(_userContext.Calls);
    }

    [Fact]
    public async Task Failing_explorer_start_gives_the_warning()
    {
        _processes.Enqueue("taskkill.exe", Ok());
        for (var i = 0; i < 40; i++)
            _processes.Enqueue("tasklist.exe", Ok(NoTasks));
        _userContext.Result = new ProcessResult(1, "", "", TimeSpan.Zero, false);

        var result = await Step(Restarting("interface.a")).ExecuteAsync(_ctx, Ct);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Equal(Strings.Get("Warning_ExplorerRestartFailed"), result.Messages[^1]);
        Assert.Equal(3, _processes.Calls.Count(c => c.FileName == "tasklist.exe"));
    }

    [Fact]
    public async Task Tasklist_that_always_fails_gives_the_warning()
    {
        _processes.Enqueue("taskkill.exe", Ok());
        for (var i = 0; i < 40; i++)
            _processes.Enqueue("tasklist.exe", new ProcessResult(1, "", "err", TimeSpan.Zero, false));

        var result = await Step(Restarting("interface.a")).ExecuteAsync(_ctx, Ct);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Equal(Strings.Get("Warning_ExplorerRestartFailed"), result.Messages[^1]);
    }

    [Fact]
    public async Task First_poll_waits_so_a_dying_explorer_is_not_seen()
    {
        _processes.Enqueue("taskkill.exe", Ok());
        _processes.Enqueue("tasklist.exe", Ok(ExplorerLine));
        var seenDelays = -1;
        _processes.OnRun = spec => { if (spec.FileName == "tasklist.exe") seenDelays = _delays.Count; };

        await Step(Restarting("interface.a")).ExecuteAsync(_ctx, Ct);

        Assert.Equal(1, seenDelays);
    }

    [Fact]
    public async Task Other_image_names_containing_explorer_do_not_count()
    {
        _processes.Enqueue("taskkill.exe", Ok());
        for (var i = 0; i < 40; i++)
            _processes.Enqueue("tasklist.exe", Ok("\"notexplorer.exe\",\"1\",\"Console\",\"1\",\"1 K\""));

        var result = await Step(Restarting("interface.a")).ExecuteAsync(_ctx, Ct);

        Assert.Equal(Strings.Get("Warning_ExplorerRestartFailed"), result.Messages[^1]);
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

    // Final review M3: with the spooler disabled Get-Printer fails, which counts as "printer installed".
    [Fact]
    public async Task Applied_noPrinter_item_is_already_in_place_even_when_the_listing_fails()
    {
        _printers.Failure = new InvalidOperationException("Get-Printer failed: the spooler is not running");
        _registry.Set(@"HKLM\Software\Test", "performance.spooler", RegistryKind.Dword, "0");
        var item = Item("performance.spooler") with { Condition = ItemCondition.NoPrinter };

        var step = Step(item);
        var result = await step.ExecuteAsync(_ctx, Ct);

        Assert.True(await step.CheckAsync(_ctx, Ct));
        Assert.Equal(StepOutcome.Skipped, result.Outcome);
        Assert.Equal([Message(item, Strings.Get("Status_AlreadyInPlace"))], result.Messages);
    }

    [Fact]
    public async Task Applied_noPrinter_item_is_already_in_place_when_a_printer_is_listed()
    {
        _printers.Names.Add("HP LaserJet");
        _registry.Set(@"HKLM\Software\Test", "performance.spooler", RegistryKind.Dword, "0");
        var item = Item("performance.spooler") with { Condition = ItemCondition.NoPrinter };

        var step = Step(item);
        var result = await step.ExecuteAsync(_ctx, Ct);

        Assert.True(await step.CheckAsync(_ctx, Ct));
        Assert.Equal([Message(item, Strings.Get("Status_AlreadyInPlace"))], result.Messages);
    }

    [Fact]
    public async Task Printers_are_not_listed_when_no_item_has_the_condition()
    {
        await Step(Item("performance.a")).ExecuteAsync(_ctx, Ct);

        Assert.Equal(0, _printers.Calls);
    }
}
