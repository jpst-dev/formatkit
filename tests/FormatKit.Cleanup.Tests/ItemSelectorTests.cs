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
}
