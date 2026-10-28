using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Cleanup.Localization;
using FormatKit.Core.Steps;

namespace FormatKit.Cleanup.Steps;

/// <summary>Applies the selected items of one category (spec 8.1).</summary>
/// <param name="includedIds">Ids the profile includes explicitly; a laptop-sensitive item among them applies even on a battery.</param>
internal sealed class CleanupCategoryStep(
    CleanupCategory category,
    IReadOnlyList<CleanupItem> items,
    IReadOnlySet<string> includedIds,
    ItemApplier applier) : IStep
{
    /// <summary>The items this step applies, for the dry-run listing.</summary>
    public IReadOnlyList<CleanupItem> Items => items;

    public string Id => $"cleanup.{category.ToString().ToLowerInvariant()}";

    public string Title => Strings.Get($"Step_Title_{category}");

    public bool RequiresAdmin => true;

    public bool IsCritical => false;

    /// <summary>False when any item is ineligible here, so <see cref="ExecuteAsync"/> runs and reports the skip reason (spec 8.3).</summary>
    public async Task<bool> CheckAsync(StepContext ctx, CancellationToken ct)
    {
        var hasBattery = await HasBatteryAsync(ctx, ct).ConfigureAwait(false);
        foreach (var item in items)
        {
            if (SkipReason(item, hasBattery, ctx) is not null)
                return false;

            try
            {
                if (!await applier.IsAppliedAsync(item, ctx, ct).ConfigureAwait(false))
                    return false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // ExecuteAsync reports this item as failed; the other items still run.
                ctx.Log.Warning(ex, "Could not check cleanup item {Item}", item.Id);
                return false;
            }
        }

        return true;
    }

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var hasBattery = await HasBatteryAsync(ctx, ct).ConfigureAwait(false);
        var messages = new List<string>();
        bool anyFailed = false, anyApplied = false, reboot = false;

        foreach (var item in items)
        {
            var title = item.LocalizedText().Title;
            var skip = SkipReason(item, hasBattery, ctx);
            if (skip is not null)
            {
                ctx.Log.Warning("Cleanup item {Item} skipped ({Reason})", item.Id, skip);
                messages.Add($"{title}: {Strings.Get(skip)}");
                continue;
            }

            ItemOutcome outcome;
            try
            {
                outcome = await applier.ApplyAsync(item, ctx, ct).ConfigureAwait(false);
            }
            catch (JournalCorruptException ex)
            {
                ctx.Log.Error(ex, "Cleanup journal {Path} is corrupt; no cleanup item is applied", ex.Path);
                return new StepResult(StepOutcome.Failed, messages, Strings.Format("NextAction_JournalCorrupt", ex.Path));
            }

            messages.Add($"{title}: {outcome.Message}");
            anyFailed |= outcome.Status == ItemStatus.Failed;
            if (outcome.Status == ItemStatus.Applied)
            {
                anyApplied = true;
                reboot |= item.RequiresReboot;
            }
        }

        if (anyFailed)
            return new StepResult(StepOutcome.Failed, messages, Strings.Get("NextAction_SeeLog"));
        if (reboot)
            return new StepResult(StepOutcome.RebootRequired, messages);
        return new StepResult(anyApplied ? StepOutcome.Succeeded : StepOutcome.Skipped, messages);
    }

    /// <summary>Asks the hardware only when some item depends on the answer.</summary>
    private async Task<bool> HasBatteryAsync(StepContext ctx, CancellationToken ct) =>
        items.Any(i => i.LaptopSensitive && !includedIds.Contains(i.Id))
        && await ctx.Services.Hardware.HasBatteryAsync(ct).ConfigureAwait(false);

    /// <summary>The resource key of the reason the item cannot apply here, or null when it is eligible.</summary>
    private string? SkipReason(CleanupItem item, bool hasBattery, StepContext ctx)
    {
        if (item.LaptopSensitive && hasBattery && !includedIds.Contains(item.Id))
            return "Skip_Battery";
        if (item.Scope == ItemScope.User && !ctx.Services.UserSession.IsConsoleUser)
            return "Skip_OtherUser";
        return null;
    }
}
