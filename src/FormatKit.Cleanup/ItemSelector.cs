using FormatKit.Cleanup.Catalog;

namespace FormatKit.Cleanup;

internal static class ItemSelector
{
    /// <summary>Items at or below the level, plus <c>include</c>, minus <c>exclude</c>, in catalog order.</summary>
    public static IReadOnlyList<CleanupItem> Select(CleanupCatalog catalog, CleanupOptions options)
    {
        var include = new HashSet<string>(options.Include, StringComparer.Ordinal);
        var exclude = new HashSet<string>(options.Exclude, StringComparer.Ordinal);
        return catalog.Items
            .Where(i => (options.Level is { } level && i.Level <= level) || include.Contains(i.Id))
            .Where(i => !exclude.Contains(i.Id))
            .ToList();
    }
}
