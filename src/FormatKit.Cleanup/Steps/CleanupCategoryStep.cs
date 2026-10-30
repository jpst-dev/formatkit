using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Cleanup.Localization;
using FormatKit.Core;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Cleanup.Steps;

/// <summary>Applies the selected items of one category (spec 8.1).</summary>
/// <param name="includedIds">Ids the profile includes explicitly; a laptop-sensitive item among them applies even on a battery.</param>
internal sealed class CleanupCategoryStep(
    CleanupCategory category,
    IReadOnlyList<CleanupItem> items,
    IReadOnlySet<string> includedIds,
    ItemApplier applier,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IStep
{
    /// <summary>The items this step applies, for the dry-run listing.</summary>
    public IReadOnlyList<CleanupItem> Items => items;

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public string Id => $"cleanup.{category.ToString().ToLowerInvariant()}";

    public string Title => Strings.Get($"Step_Title_{category}");

    public bool RequiresAdmin => true;

    public bool IsCritical => false;

    /// <summary>False when any item is ineligible here, so <see cref="ExecuteAsync"/> runs and reports the skip reason (spec 8.3).</summary>
    public async Task<bool> CheckAsync(StepContext ctx, CancellationToken ct)
    {
        var hasBattery = await HasBatteryAsync(ctx, ct).ConfigureAwait(false);
        var printerInstalled = await PrinterInstalledAsync(ctx, ct).ConfigureAwait(false);
        foreach (var item in items)
        {
            if (SkipReason(item, hasBattery, printerInstalled, ctx) is not null)
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
        var printerInstalled = await PrinterInstalledAsync(ctx, ct).ConfigureAwait(false);
        var messages = new List<string>();
        bool anyFailed = false, anyApplied = false, reboot = false, restartExplorer = false;

        foreach (var item in items)
        {
            var title = item.LocalizedText().Title;
            var skip = SkipReason(item, hasBattery, printerInstalled, ctx);
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
                restartExplorer |= item.RestartsExplorer;
            }
        }

        string? nextAction = null;
        if (restartExplorer && !await RestartExplorerAsync(ctx, ct).ConfigureAwait(false))
        {
            ctx.Log.Warning("Explorer did not reappear after the restart");
            messages.Add(Strings.Get("Warning_ExplorerRestartFailed"));
            nextAction = Strings.Get("NextAction_SignOutAndIn");
        }

        if (anyFailed)
            return new StepResult(StepOutcome.Failed, messages, Strings.Get("NextAction_SeeLog"));
        if (reboot)
            return new StepResult(StepOutcome.RebootRequired, messages, nextAction);
        return new StepResult(anyApplied ? StepOutcome.Succeeded : StepOutcome.Skipped, messages, nextAction);
    }

    /// <summary>Asks the hardware only when some item depends on the answer.</summary>
    private async Task<bool> HasBatteryAsync(StepContext ctx, CancellationToken ct) =>
        items.Any(i => i.LaptopSensitive && !includedIds.Contains(i.Id))
        && await ctx.Services.Hardware.HasBatteryAsync(ct).ConfigureAwait(false);

    /// <summary>
    /// Kills Explorer and waits a moment for Windows (Winlogon's AutoRestartShell) to start it again. When it does not (seen
    /// in the VM), it is started as the signed-in user through the limited-run-level user-context runner: started from this
    /// elevated process it would be an elevated shell. <c>cmd /c start</c> returns at once, so the runner does not wait on
    /// the shell. False when Explorer did not come back.
    /// </summary>
    private async Task<bool> RestartExplorerAsync(StepContext ctx, CancellationToken ct)
    {
        try
        {
            var kill = await ctx.Services.Processes.RunAsync(
                new ProcessSpec("taskkill.exe", ["/f", "/im", "explorer.exe"], Timeouts.Query), ct).ConfigureAwait(false);
            if (kill.ExitCode != 0 || kill.TimedOut)
            {
                ctx.Log.Warning("taskkill explorer.exe failed (exit {Exit}): {Error}", kill.ExitCode, kill.StdErr.Trim());
                return false;
            }

            if (await WaitForExplorerAsync(ctx, AutoRestartPolls, ct).ConfigureAwait(false))
                return true;

            ctx.Log.Information("Explorer did not restart by itself; starting it as the signed-in user");
            var start = await ctx.Services.UserContext.RunAsync(
                new ProcessSpec("cmd.exe", ["/c", "start", "explorer.exe"], Timeouts.Query), ct).ConfigureAwait(false);
            if (start.ExitCode != 0 || start.TimedOut)
            {
                ctx.Log.Warning("Starting explorer.exe as the signed-in user failed (exit {Exit})", start.ExitCode);
                return false;
            }

            return await WaitForExplorerAsync(ctx, StartedPolls, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ctx.Log.Warning(ex, "Could not restart Explorer");
            return false;
        }
    }

    private async Task<bool> WaitForExplorerAsync(StepContext ctx, int polls, CancellationToken ct)
    {
        // Wait first: right after a forced kill the dying process can still be listed.
        for (var poll = 0; poll < polls; poll++)
        {
            await _delay(ExplorerPollInterval, ct).ConfigureAwait(false);
            var list = await ctx.Services.Processes.RunAsync(
                new ProcessSpec("tasklist.exe", ["/fi", "imagename eq explorer.exe", "/fo", "csv", "/nh"], Timeouts.Query), ct).ConfigureAwait(false);
            if (list.ExitCode == 0 && list.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Any(l => l.TrimStart().StartsWith("\"explorer.exe\"", StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    private const int AutoRestartPolls = 3;
    private const int StartedPolls = 15;
    private static readonly TimeSpan ExplorerPollInterval = TimeSpan.FromSeconds(1);

    private static readonly string[] VirtualPrinters = ["Microsoft Print to PDF", "Microsoft XPS Document Writer", "Fax"];

    /// <summary>
    /// True when a physical printer may be installed: asked only when an item has the noPrinter condition. A failing listing
    /// counts as installed, the safe side (the spooler stays on).
    /// </summary>
    private async Task<bool> PrinterInstalledAsync(StepContext ctx, CancellationToken ct)
    {
        if (!items.Any(i => i.Condition == ItemCondition.NoPrinter))
            return false;
        try
        {
            var names = await ctx.Services.Printers.ListNamesAsync(ct).ConfigureAwait(false);
            return names.Any(n => !VirtualPrinters.Contains(n, StringComparer.OrdinalIgnoreCase)
                && !n.StartsWith("OneNote", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ctx.Log.Warning(ex, "Could not list printers; assuming one is installed");
            return true;
        }
    }

    /// <summary>The resource key of the reason the item cannot apply here, or null when it is eligible.</summary>
    private string? SkipReason(CleanupItem item, bool hasBattery, bool printerInstalled, StepContext ctx)
    {
        if (item.LaptopSensitive && hasBattery && !includedIds.Contains(item.Id))
            return "Skip_Battery";
        if (item.Scope == ItemScope.User && !ctx.Services.UserSession.IsConsoleUser)
            return "Skip_OtherUser";
        if (item.Condition == ItemCondition.NoPrinter && printerInstalled)
            return "Skip_PrinterInstalled";
        return null;
    }
}
