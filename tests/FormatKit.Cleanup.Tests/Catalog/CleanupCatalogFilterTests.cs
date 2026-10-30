using FormatKit.Cleanup.Catalog;
using static FormatKit.Cleanup.Tests.TestItems;

namespace FormatKit.Cleanup.Tests.Catalog;

public class CleanupCatalogFilterTests
{
    private static readonly CleanupCatalog TestCatalog = NewCatalog(
        Item("privacy.safe", CleanupLevel.Safe),
        Item("apps.balanced", CleanupLevel.Balanced),
        Item("developer.aggressive", CleanupLevel.Aggressive),
        Item("performance.aggressive", CleanupLevel.Aggressive));

    private static IEnumerable<string> Ids(CleanupLevel? level, CleanupCategory? category = null) =>
        TestCatalog.Filter(level, category).Select(i => i.Id);

    [Fact]
    public void NoFilter_ReturnsEverythingInCatalogOrder() =>
        Assert.Equal(TestCatalog.Items.Select(i => i.Id), Ids(null));

    [Fact]
    public void Level_IsCumulative()
    {
        Assert.Equal(["privacy.safe"], Ids(CleanupLevel.Safe));
        Assert.Equal(["privacy.safe", "apps.balanced"], Ids(CleanupLevel.Balanced));
        Assert.Equal(4, Ids(CleanupLevel.Aggressive).Count());
    }

    [Fact]
    public void Category_FiltersAndCombinesWithLevel()
    {
        Assert.Equal(["developer.aggressive"], Ids(null, CleanupCategory.Developer));
        Assert.Empty(Ids(CleanupLevel.Safe, CleanupCategory.Developer));
    }
}
