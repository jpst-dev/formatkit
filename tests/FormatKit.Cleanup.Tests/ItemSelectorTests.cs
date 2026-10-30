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

    [Fact]
    public void Included_power_scheme_item_wins_over_the_levels_item()
    {
        var catalog = NewCatalog(
            Item("performance.high", CleanupLevel.Balanced, actions: new PowerSchemeAction("highPerformance")),
            Item("performance.formatkit", CleanupLevel.Aggressive, actions: new PowerSchemeAction("formatkit")) with { OptIn = true });

        var included = ItemSelector.Select(catalog, new CleanupOptions(true, CleanupLevel.Aggressive, ["performance.formatkit"], [], true));
        var notIncluded = ItemSelector.Select(catalog, new CleanupOptions(true, CleanupLevel.Aggressive, [], [], true));
        var lowLevel = ItemSelector.Select(catalog, new CleanupOptions(true, CleanupLevel.Balanced, ["performance.formatkit"], [], true));

        Assert.Equal(["performance.formatkit"], included.Select(i => i.Id));
        Assert.Equal(["performance.high"], notIncluded.Select(i => i.Id));
        Assert.Equal(["performance.formatkit"], lowLevel.Select(i => i.Id));
    }

    [Fact]
    public void Including_an_item_the_level_already_selects_does_not_change_which_plan_wins()
    {
        var catalog = NewCatalog(
            Item("performance.high", CleanupLevel.Balanced, actions: new PowerSchemeAction("highPerformance")),
            Item("performance.ultimate", CleanupLevel.Aggressive, actions: new PowerSchemeAction("ultimate")));

        var includedHigh = ItemSelector.Select(catalog, new CleanupOptions(true, CleanupLevel.Aggressive, ["performance.high"], [], true));
        var includedUltimateLow = ItemSelector.Select(catalog, new CleanupOptions(true, CleanupLevel.Balanced, ["performance.ultimate"], [], true));

        Assert.Equal(["performance.ultimate"], includedHigh.Select(i => i.Id));
        Assert.Equal(["performance.ultimate"], includedUltimateLow.Select(i => i.Id));
    }

    [Fact]
    public void Only_the_last_item_for_the_same_power_setting_is_kept()
    {
        const string display = "7516b95f-f776-4464-8c53-06167f40cc99";
        const string videoIdle = "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e";
        const string sleep = "238c9fa8-0aad-41ed-83f4-97be242c8f20";
        const string standbyIdle = "29f6c1db-86da-48c5-9fdb-f2b67b1f44da";
        var catalog = NewCatalog(
            Item("power.monitor-off-15", CleanupLevel.Safe, actions: new PowerSettingAction(display, videoIdle, 900, null)),
            Item("power.sleep-never", CleanupLevel.Safe, actions: new PowerSettingAction(sleep, standbyIdle, 0, null)),
            Item("power.monitor-off-30", CleanupLevel.Safe, actions: new PowerSettingAction(display.ToUpperInvariant(), videoIdle, 1800, null)));
        var log = new CollectingSink();
        var logger = new Serilog.LoggerConfiguration().WriteTo.Sink(log).CreateLogger();

        var selected = ItemSelector.Select(catalog, new CleanupOptions(true, CleanupLevel.Safe, [], [], false), logger);

        Assert.Equal(["power.sleep-never", "power.monitor-off-30"], selected.Select(i => i.Id));
        var dropped = Assert.Single(log.Events);
        Assert.Contains("power.monitor-off-15", dropped.RenderMessage());
    }

    private sealed class CollectingSink : Serilog.Core.ILogEventSink
    {
        public List<Serilog.Events.LogEvent> Events { get; } = [];

        public void Emit(Serilog.Events.LogEvent logEvent) => Events.Add(logEvent);
    }
}
