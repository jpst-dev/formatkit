using FormatKit.Cleanup.Catalog;
using static FormatKit.Cleanup.Tests.TestItems;

namespace FormatKit.Cleanup.Tests;

public class ItemSelectorTests
{
    private static readonly CleanupCatalog TestCatalog = NewCatalog(
        Item("privacy.safe", CleanupLevel.Safe),
        Item("apps.balanced", CleanupLevel.Balanced),
        Item("performance.aggressive", CleanupLevel.Aggressive));

    private static IEnumerable<string> Select(CleanupLevel? level, string[]? include = null, string[]? exclude = null) =>
        ItemSelector.Select(TestCatalog, new CleanupOptions(true, level, include ?? [], exclude ?? [], false)).Select(i => i.Id);

    [Fact]
    public void Level_is_cumulative()
    {
        Assert.Equal(["privacy.safe"], Select(CleanupLevel.Safe));
        Assert.Equal(["privacy.safe", "apps.balanced"], Select(CleanupLevel.Balanced));
        Assert.Equal(["privacy.safe", "apps.balanced", "performance.aggressive"], Select(CleanupLevel.Aggressive));
    }

    [Fact]
    public void Include_adds_higher_level_item() =>
        Assert.Equal(["privacy.safe", "performance.aggressive"], Select(CleanupLevel.Safe, include: ["performance.aggressive"]));

    [Fact]
    public void Exclude_removes() =>
        Assert.Equal(["privacy.safe", "performance.aggressive"], Select(CleanupLevel.Aggressive, exclude: ["apps.balanced"]));

    [Fact]
    public void Only_highest_level_power_scheme_item_is_kept()
    {
        var catalog = NewCatalog(
            Item("performance.high", CleanupLevel.Balanced, actions: new PowerSchemeAction("highPerformance")),
            Item("privacy.safe", CleanupLevel.Safe),
            Item("performance.ultimate", CleanupLevel.Aggressive, actions: new PowerSchemeAction("ultimate")));
        var log = new CollectingSink();
        var logger = new Serilog.LoggerConfiguration().WriteTo.Sink(log).CreateLogger();

        var aggressive = ItemSelector.Select(catalog, new CleanupOptions(true, CleanupLevel.Aggressive, [], [], true), logger);
        var balanced = ItemSelector.Select(catalog, new CleanupOptions(true, CleanupLevel.Balanced, [], [], false), logger);

        Assert.Equal(["privacy.safe", "performance.ultimate"], aggressive.Select(i => i.Id));
        Assert.Equal(["performance.high", "privacy.safe"], balanced.Select(i => i.Id));
        var dropped = Assert.Single(log.Events);
        Assert.Equal(Serilog.Events.LogEventLevel.Information, dropped.Level);
        Assert.Contains("performance.high", dropped.RenderMessage());
    }

    private sealed class CollectingSink : Serilog.Core.ILogEventSink
    {
        public List<Serilog.Events.LogEvent> Events { get; } = [];

        public void Emit(Serilog.Events.LogEvent logEvent) => Events.Add(logEvent);
    }
}
