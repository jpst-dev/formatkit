using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Cleanup.Localization;
using FormatKit.Core;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Cleanup;

/// <summary>Reverts what cleanup recorded in its journal (spec 8.2). <see cref="ItemStatus.Applied"/> in an outcome means the item was reverted.</summary>
/// <param name="catalog">Kept for symmetry with <see cref="CleanupModule"/>; undo reads everything it needs from the journal, so items removed from the catalog stay undoable.</param>
/// <param name="files">The file system holding the journal; the real one unless a test supplies another.</param>
#pragma warning disable CS9113 // catalog is part of the agreed constructor (R9) but unused
public sealed class CleanupUndo(CleanupCatalog catalog, AppPaths paths, IFileSystem? files = null)
#pragma warning restore CS9113
{
    private const int JournalSchemaVersion = 1;

    /// <summary>Undoes <paramref name="itemIds"/> (all items with unreverted entries when null), newest first.</summary>
    /// <exception cref="JournalCorruptException">The journal cannot be read; nothing is changed.</exception>
    public async Task<IReadOnlyList<ItemOutcome>> UndoAsync(IReadOnlyList<string>? itemIds, StepContext ctx, CancellationToken ct)
    {
        var store = new JournalStore(files ?? new FileSystem(), paths);
        var loaded = store.Load();
        if (loaded.Status == JournalLoadStatus.Corrupt)
            throw new JournalCorruptException(store.Path, loaded.Error ?? "unknown error");
        var journal = loaded.Journal ?? new CleanupJournal(JournalSchemaVersion, []);

        var handlers = new ActionHandlers(() => PowerSchemeHandler.CreatedSchemes(store.Load().Journal));

        var latest = journal.Entries
            .Where(e => e.RevertedAt is null)
            .GroupBy(e => e.ItemId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(e => e.AppliedAt), StringComparer.Ordinal);

        var ids = (itemIds ?? latest.Keys.ToList()).Distinct(StringComparer.Ordinal)
            .OrderByDescending(id => latest.TryGetValue(id, out var at) ? at : DateTimeOffset.MinValue)
            .ToList();

        var outcomes = new List<ItemOutcome>();
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            if (!latest.ContainsKey(id))
            {
                outcomes.Add(new ItemOutcome(id, ItemStatus.Skipped, Strings.Get("Undo_NothingToUndo")));
                continue;
            }

            outcomes.Add(await UndoItemAsync(id, journal, store, handlers, ctx, ct).ConfigureAwait(false));
        }

        return outcomes;
    }

    private async Task<ItemOutcome> UndoItemAsync(string id, CleanupJournal journal, JournalStore store, ActionHandlers handlers, StepContext ctx, CancellationToken ct)
    {
        var indexes = Enumerable.Range(0, journal.Entries.Count)
            .Where(i => journal.Entries[i].ItemId == id && journal.Entries[i].RevertedAt is null)
            .OrderBy(i => journal.Entries[i].AppliedAt)
            .ToList();

        // The earliest entry per target holds the state before cleanup touched it; later entries only captured our own changes.
        var targets = indexes
            .SelectMany(i => journal.Entries[i].Actions)
            .GroupBy(a => (a.Type, a.Target))
            .Select(g => g.First())
            .Reverse()
            .ToList();

        var failures = new List<string>();
        foreach (var action in targets)
        {
            try
            {
                await handlers.For(action).UndoAsync(action, ctx, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                ctx.Log.Warning(ex, "Could not undo {Type} {Target} of cleanup item {Item}", action.Type, action.Target, id);
                failures.Add(ex.Message);
            }
        }

        if (failures.Count > 0)
            return new ItemOutcome(id, ItemStatus.Failed, Strings.Format("Status_Failed", string.Join("; ", failures)));

        var now = DateTimeOffset.Now;
        foreach (var i in indexes)
            journal.Entries[i] = journal.Entries[i] with { RevertedAt = now };
        store.Save(journal);

        ctx.Log.Information("Cleanup item {Item} reverted", id);
        return new ItemOutcome(id, ItemStatus.Applied, Strings.Get("Undo_Reverted"));
    }
}
