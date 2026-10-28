using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;

namespace FormatKit.Cleanup;

internal static class ItemSelector
{
    /// <summary>Items at or below the level, plus <c>include</c>, minus <c>exclude</c>, in catalog order.</summary>
    /// <remarks>Power scheme items share one target, so only the highest-level one is kept (R14); <paramref name="log"/> hears which were dropped.</remarks>
    public static IReadOnlyList<CleanupItem> Select(CleanupCatalog catalog, CleanupOptions options, Serilog.ILogger? log = null)
    {
        var include = new HashSet<string>(options.Include, StringComparer.Ordinal);
        var exclude = new HashSet<string>(options.Exclude, StringComparer.Ordinal);
        var selected = catalog.Items
            .Where(i => (options.Level is { } level && i.Level <= level) || include.Contains(i.Id))
            .Where(i => !exclude.Contains(i.Id))
            .ToList();

        var power = selected.Where(i => i.Actions.Any(a => a is PowerSchemeAction)).ToList();
        if (power.Count <= 1)
            return selected;

        var kept = power.Aggregate((best, next) => next.Level >= best.Level ? next : best);
        foreach (var dropped in power.Where(i => i != kept))
        {
            log?.Information("Cleanup item {Dropped} not selected: {Kept} sets the power scheme at a higher level", dropped.Id, kept.Id);
            selected.Remove(dropped);
        }

        return selected;
    }
}
