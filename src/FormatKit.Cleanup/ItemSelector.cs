using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;

namespace FormatKit.Cleanup;

internal static class ItemSelector
{
    /// <summary>Items at or below the level, plus <c>include</c> (the only way to select an opt-in or developer item), minus <c>exclude</c>, in catalog order.</summary>
    /// <remarks>Power scheme items share one target, so only one is kept (R14): an item selected only by <c>include</c> beats one the level selected, then the highest level wins; <paramref name="log"/> hears which were dropped.</remarks>
    public static IReadOnlyList<CleanupItem> Select(CleanupCatalog catalog, CleanupOptions options, Serilog.ILogger? log = null)
    {
        var include = new HashSet<string>(options.Include, StringComparer.Ordinal);
        var exclude = new HashSet<string>(options.Exclude, StringComparer.Ordinal);
        bool SelectedByLevel(CleanupItem i) => options.Level is { } level && i.Level <= level && !i.IsOptIn;
        var selected = catalog.Items
            .Where(i => SelectedByLevel(i) || include.Contains(i.Id))
            .Where(i => !exclude.Contains(i.Id))
            .ToList();

        DropDuplicatePowerSettings(selected, log);

        var power = selected.Where(i => i.Actions.Any(a => a is PowerSchemeAction)).ToList();
        if (power.Count <= 1)
            return selected;

        // Only an item the level would not have selected counts as an explicit choice; otherwise the higher level wins as before.
        bool OnlyByInclude(CleanupItem i) => include.Contains(i.Id) && !SelectedByLevel(i);
        var kept = power.Aggregate((best, next) =>
            (OnlyByInclude(next), next.Level).CompareTo((OnlyByInclude(best), best.Level)) >= 0 ? next : best);
        foreach (var dropped in power.Where(i => i != kept))
        {
            var rule = OnlyByInclude(kept) && !OnlyByInclude(dropped) ? "it was explicitly included" : "it has a higher level";
            log?.Information("Cleanup item {Dropped} not selected: {Kept} sets the power scheme and {Rule}", dropped.Id, kept.Id, rule);
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
