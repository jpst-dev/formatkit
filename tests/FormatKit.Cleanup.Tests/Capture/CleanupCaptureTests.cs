using System.Text.Json;
using FormatKit.Cleanup.Capture;
using FormatKit.Cleanup.Catalog;
using FormatKit.Core;
using FormatKit.Core.Capture;
using Serilog.Core;
using static FormatKit.Cleanup.Tests.TestItems;

namespace FormatKit.Cleanup.Tests.Capture;

public class CleanupCaptureTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<CaptureSection> Run(CleanupCatalog catalog, Func<CleanupItem, bool> applied) =>
        CleanupCapture.RunAsync(catalog, item => Task.FromResult(applied(item)), Logger.None, Ct);

    private static IEnumerable<string> Include(CaptureSection s) =>
        s.Value["include"]!.AsArray().Select(n => n!.GetValue<string>());

    [Fact]
    public async Task Applied_items_go_to_include_in_catalog_order_with_no_level()
    {
        var catalog = NewCatalog(Item("privacy.a"), Item("privacy.b"), Item("privacy.c"));

        var section = await Run(catalog, i => i.Id is "privacy.c" or "privacy.a");

        Assert.True(section.Value["enabled"]!.GetValue<bool>());
        Assert.Equal(["privacy.a", "privacy.c"], Include(section));
        Assert.Null(section.Value["level"]);
    }

    [Fact]
    public async Task Applied_aggressive_item_is_reported_not_captured()
    {
        var catalog = NewCatalog(Item("privacy.safe"), Item("privacy.hard") with { Level = CleanupLevel.Aggressive });

        var section = await Run(catalog, _ => true);

        Assert.Equal(["privacy.safe"], Include(section));
        Assert.Null(section.Value["confirmAggressive"]);
        Assert.Contains(section.Notes, n => n.Kind == CaptureNoteKind.Review && n.Text.Contains("privacy.hard"));
    }

    [Fact]
    public async Task Applied_contested_item_is_captured_and_flagged_for_review()
    {
        var catalog = NewCatalog(Item("privacy.debated") with { Contested = true });

        var section = await Run(catalog, _ => true);

        Assert.Equal(["privacy.debated"], Include(section));
        Assert.Contains(section.Notes, n => n.Kind == CaptureNoteKind.Review && n.Text.Contains("privacy.debated"));
    }

    [Fact]
    public async Task A_check_that_throws_becomes_not_checked()
    {
        var catalog = NewCatalog(Item("privacy.denied"), Item("privacy.fine"));

        var section = await CleanupCapture.RunAsync(
            catalog,
            item => item.Id == "privacy.denied" ? throw new UnauthorizedAccessException("no") : Task.FromResult(true),
            Logger.None,
            Ct);

        Assert.Equal(["privacy.fine"], Include(section));
        Assert.Contains(section.Notes, n => n.Kind == CaptureNoteKind.NotCaptured && n.Text.Contains("privacy.denied"));
    }

    /// <summary>The console shows warnings, and the report already says this; a run without elevation would print one line per app.</summary>
    [Fact]
    public async Task A_check_that_throws_logs_nothing_at_warning_level()
    {
        var events = new List<Serilog.Events.LogEvent>();
        var log = new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(new ListSink(events)).CreateLogger();

        await CleanupCapture.RunAsync(NewCatalog(Item("privacy.denied")), _ => throw new UnauthorizedAccessException("no"), log, Ct);

        var logged = Assert.Single(events);
        Assert.True(logged.Level < Serilog.Events.LogEventLevel.Warning);
        Assert.NotNull(logged.Exception);
    }

    private sealed class ListSink(List<Serilog.Events.LogEvent> events) : Serilog.Core.ILogEventSink
    {
        public void Emit(Serilog.Events.LogEvent logEvent) => events.Add(logEvent);
    }

    [Fact]
    public async Task An_absent_target_is_left_out_without_a_note()
    {
        var section = await CleanupCapture.RunAsync(
            NewCatalog(Item("privacy.gone")),
            _ => throw new FormatKit.Cleanup.Actions.TargetNotPresentException("gone"),
            Logger.None,
            Ct);

        Assert.False(section.Value["enabled"]!.GetValue<bool>());
        Assert.Empty(section.Notes);
    }

    [Fact]
    public async Task Cancellation_passes_through() =>
        await Assert.ThrowsAsync<OperationCanceledException>(() => CleanupCapture.RunAsync(
            NewCatalog(Item("privacy.a")), _ => throw new OperationCanceledException(), Logger.None, Ct));

    [Fact]
    public async Task Nothing_applied_disables_the_section()
    {
        var section = await Run(NewCatalog(Item("privacy.a")), _ => false);

        Assert.False(section.Value["enabled"]!.GetValue<bool>());
        Assert.Null(section.Value["include"]);
    }

    [Fact]
    public async Task Captured_section_passes_module_validation()
    {
        var catalog = NewCatalog(
            Item("privacy.plain"),
            Item("privacy.opted") with { OptIn = true },
            Item("privacy.debated") with { Contested = true },
            Item("privacy.hard") with { Level = CleanupLevel.Aggressive });

        var section = await Run(catalog, _ => true);

        using var doc = JsonDocument.Parse(section.Value.ToJsonString());
        var module = new CleanupModule(catalog, new AppPaths(@"C:\FormatKitTest"));
        Assert.Empty(module.Validate(doc.RootElement, "modules.cleanup"));
    }

    [Fact]
    public async Task Disabled_section_passes_module_validation()
    {
        var catalog = NewCatalog(Item("privacy.a"));

        var section = await Run(catalog, _ => false);

        using var doc = JsonDocument.Parse(section.Value.ToJsonString());
        Assert.Empty(new CleanupModule(catalog, new AppPaths(@"C:\FormatKitTest")).Validate(doc.RootElement, "modules.cleanup"));
    }
}
