using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;

namespace FormatKit.Cleanup;

internal static class ItemSelector
{
    /// <summary>Items at or below the level, plus <c>include</c> (the only way to select an opt-in or developer item), minus <c>exclude</c>, in catalog order.</summary>
    /// <remarks>Power scheme items share one target, so only one is kept (R14): an item named in <c>include</c> beats one the level selected, then the highest level wins; <paramref name="log"/> hears which were dropped.</remarks>
    public static IReadOnlyList<CleanupItem> Select(CleanupCatalog catalog, CleanupOptions options, Serilog.ILogger? log = null)
    {
        var include = new HashSet<string>(options.Include, StringComparer.Ordinal);
        var exclude = new HashSet<string>(options.Exclude, StringComparer.Ordinal);
        var selected = catalog.Items
            .Where(i => (options.Level is { } level && i.Level <= level && !i.IsOptIn) || include.Contains(i.Id))
            .Where(i => !exclude.Contains(i.Id))
            .ToList();

        DropDuplicatePowerSettings(selected, log);

        var power = selected.Where(i => i.Actions.Any(a => a is PowerSchemeAction)).ToList();
        if (power.Count <= 1)
            return selected;

        var kept = power.Aggregate((best, next) =>
            (include.Contains(next.Id), next.Level).CompareTo((include.Contains(best.Id), best.Level)) >= 0 ? next : best);
        foreach (var dropped in power.Where(i => i != kept))
        {
            log?.Information("Cleanup item {Dropped} not selected: {Kept} sets the power scheme at a higher level or was included", dropped.Id, kept.Id);
            selected.Remove(dropped);
        }

        return selected;
    }

    /// <summary>Items that set the same power setting conflict; only the last one in catalog order is kept.</summary>
    private static void DropDuplicatePowerSettings(List<CleanupItem> selected, Serilog.ILogger? log)
    {
        static IEnumerable<(string, string)> Settings(CleanupItem item) => item.Actions
            .OfType<PowerSettingAction>()
            .Select(a => (a.Subgroup.ToLowerInvariant(), a.Setting.ToLowerInvariant()));

        for (var i = selected.Count - 1; i >= 0; i--)
        {
            var item = selected[i];
            var later = selected.Skip(i + 1).FirstOrDefault(other => Settings(other).Intersect(Settings(item)).Any());
            if (later is null)
                continue;
            log?.Information("Cleanup item {Dropped} not selected: {Kept} sets the same power setting later in the catalog", item.Id, later.Id);
            selected.RemoveAt(i);
        }
    }
}
