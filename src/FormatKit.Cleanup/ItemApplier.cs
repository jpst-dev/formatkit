using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Cleanup.Localization;
using FormatKit.Core.Steps;

namespace FormatKit.Cleanup;

public enum ItemStatus { Applied, AlreadyInPlace, Skipped, Failed }

/// <summary>The result for one item. <paramref name="Message"/> is the localized status or reason.</summary>
public sealed record ItemOutcome(string ItemId, ItemStatus Status, string Message);

/// <summary>Applies one catalog item, journaling each change before making it (spec 8.2).</summary>
internal sealed class ItemApplier(ActionHandlers handlers, JournalStore journal)
{
    private const int JournalSchemaVersion = 1;

    public async Task<bool> IsAppliedAsync(CleanupItem item, StepContext ctx, CancellationToken ct)
    {
        foreach (var action in item.Actions)
        {
            if (!await handlers.For(action).IsAppliedAsync(action, ctx, ct).ConfigureAwait(false))
                return false;
        }

        return true;
    }

    /// <exception cref="JournalCorruptException">The journal cannot be read; nothing is changed.</exception>
    public async Task<ItemOutcome> ApplyAsync(CleanupItem item, StepContext ctx, CancellationToken ct)
    {
        var loaded = journal.Load();
        if (loaded.Status == JournalLoadStatus.Corrupt)
            throw new JournalCorruptException(journal.Path, loaded.Error ?? "unknown error");
        var current = loaded.Journal ?? new CleanupJournal(JournalSchemaVersion, []);

        var applied = new List<JournalAction>();
        JournalAction? inFlight = null;
        var entryIndex = -1;
        try
        {
            var pending = new List<CleanupAction>();
            foreach (var action in item.Actions)
            {
                if (!await handlers.For(action).IsAppliedAsync(action, ctx, ct).ConfigureAwait(false))
                    pending.Add(action);
            }

            if (pending.Count == 0)
            {
                ctx.Log.Information("Cleanup item {Item} already in place", item.Id);
                return new ItemOutcome(item.Id, ItemStatus.AlreadyInPlace, Strings.Get("Status_AlreadyInPlace"));
            }

            foreach (var action in pending)
            {
                var handler = handlers.For(action);
                var captured = await handler.CaptureAsync(action, ctx, ct).ConfigureAwait(false);
                if (entryIndex < 0)
                {
                    // HKCU values belong to the process user, which the step has already checked is the console user.
                    var userSid = item.Actions.Any(IsPerUser) ? ctx.Services.UserSession.CurrentUserSid : null;
                    current.Entries.Add(new JournalEntry(item.Id, DateTimeOffset.Now, [], null, userSid));
                    entryIndex = current.Entries.Count - 1;
                }

                var actions = current.Entries[entryIndex].Actions;
                actions.Add(captured);
                journal.Save(current);

                // A handler may change state and then throw on verify, so the capture is undone too if Apply fails.
                inFlight = captured;
                var record = await handler.ApplyAsync(action, captured, ctx, ct).ConfigureAwait(false);
                inFlight = null;
                applied.Add(record);
                actions[^1] = record;
                journal.Save(current);
            }

            ctx.Log.Information("Cleanup item {Item} applied", item.Id);
            return new ItemOutcome(item.Id, ItemStatus.Applied, Strings.Get("Status_Applied"));
        }
        catch (Exception ex)
        {
            if (inFlight is not null)
                applied.Add(inFlight);
            var rolledBack = await RollBackAsync(item, applied, ctx).ConfigureAwait(false);

            // An incomplete rollback leaves the entry unreverted so `cleanup undo` can retry it.
            if (entryIndex >= 0 && rolledBack)
            {
                current.Entries[entryIndex] = current.Entries[entryIndex] with { RevertedAt = DateTimeOffset.Now };
                try
                {
                    journal.Save(current);
                }
                catch (Exception saveError)
                {
                    ctx.Log.Warning(saveError, "Could not mark cleanup item {Item} as reverted in the journal", item.Id);
                }
            }

            if (ex is OperationCanceledException && ct.IsCancellationRequested)
                throw;

            if (ex is TargetNotPresentException)
            {
                ctx.Log.Information("Cleanup item {Item} skipped: {Reason}", item.Id, ex.Message);
                return new ItemOutcome(item.Id, ItemStatus.Skipped, Strings.Get("Skip_NotPresent"));
            }

            ctx.Log.Error(ex, "Cleanup item {Item} failed", item.Id);
            return new ItemOutcome(item.Id, ItemStatus.Failed, Strings.Format("Status_Failed", ex.Message));
        }
    }

    private static bool IsPerUser(CleanupAction action) =>
        action is RegistryAction registry && registry.Path.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase)
        || action is StartupItemAction { Scope: ItemScope.User };

    /// <summary>Undoes this attempt's actions, newest first (the failing one included). Best effort: an undo failure is logged, not thrown. Returns whether every undo succeeded.</summary>
    private async Task<bool> RollBackAsync(CleanupItem item, List<JournalAction> applied, StepContext ctx)
    {
        var complete = true;
        for (var i = applied.Count - 1; i >= 0; i--)
        {
            try
            {
                await handlers.For(applied[i]).UndoAsync(applied[i], ctx, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception undoError)
            {
                complete = false;
                ctx.Log.Warning(undoError, "Could not roll back {Type} {Target} of cleanup item {Item}", applied[i].Type, applied[i].Target, item.Id);
            }
        }

        return complete;
    }
}
