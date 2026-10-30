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
    public void Developer_items_never_selected_by_any_level()
    {
        var catalog = NewCatalog(Item("privacy.safe", CleanupLevel.Safe), Item("developer.long-paths", CleanupLevel.Safe));

        foreach (var level in Enum.GetValues<CleanupLevel>())
        {
            var selected = ItemSelector.Select(catalog, new CleanupOptions(true, level, [], [], true)).Select(i => i.Id);
            Assert.Equal(["privacy.safe"], selected);
        }
    }

    [Fact]
    public void Developer_item_selected_by_include()
    {
        var catalog = NewCatalog(Item("privacy.safe", CleanupLevel.Safe), Item("developer.long-paths", CleanupLevel.Safe));

        var selected = ItemSelector.Select(catalog, new CleanupOptions(true, CleanupLevel.Safe, ["developer.long-paths"], [], false));
        var withoutLevel = ItemSelector.Select(catalog, new CleanupOptions(true, null, ["developer.long-paths"], [], false));

        Assert.Equal(["privacy.safe", "developer.long-paths"], selected.Select(i => i.Id));
        Assert.Equal(["developer.long-paths"], withoutLevel.Select(i => i.Id));
    }

    private static CleanupItem OptInItem(string id) =>
        Item(id) with { OptIn = true };

    [Fact]
    public void Select_OptInItem_NotSelectedByLevel()
    {
        var catalog = NewCatalog(Item("privacy.safe"), OptInItem("startup.opt"));

        foreach (var level in Enum.GetValues<CleanupLevel>())
            Assert.Equal(["privacy.safe"], ItemSelector.Select(catalog, new CleanupOptions(true, level, [], [], true)).Select(i => i.Id));
    }

    [Fact]
    public void Select_OptInItem_SelectedByInclude()
    {
        var catalog = NewCatalog(Item("privacy.safe"), OptInItem("startup.opt"));

        var selected = ItemSelector.Select(catalog, new CleanupOptions(true, CleanupLevel.Safe, ["startup.opt"], [], false));

        Assert.Equal(["privacy.safe", "startup.opt"], selected.Select(i => i.Id));
    }

    [Fact]
    public void Select_DeveloperItem_StillIncludeOnly()
    {
        var developer = Item("developer.long-paths");
        Assert.True(developer.IsOptIn);
        var catalog = NewCatalog(Item("privacy.safe"), developer);

        Assert.Equal(["privacy.safe"], ItemSelector.Select(catalog, new CleanupOptions(true, CleanupLevel.Aggressive, [], [], true)).Select(i => i.Id));
        Assert.Equal(["privacy.safe", "developer.long-paths"],
            ItemSelector.Select(catalog, new CleanupOptions(true, CleanupLevel.Aggressive, ["developer.long-paths"], [], true)).Select(i => i.Id));
    }

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
