using System.Text.Json.Nodes;
using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Cleanup.Localization;
using FormatKit.Core;
using FormatKit.Core.Capture;
using FormatKit.Core.Steps;

namespace FormatKit.Cleanup.Capture;

/// <summary>Captures the catalog items already applied on this machine, as an exact <c>include</c> list with no level.</summary>
public static class CleanupCapture
{
    private const string SectionPath = "modules.cleanup";

    public static Task<CaptureSection> RunAsync(
        ServiceSet services, CleanupCatalog catalog, AppPaths paths, Serilog.ILogger log, CancellationToken ct)
    {
        var journal = new JournalStore(services.Files, paths);
        var handlers = new ActionHandlers(
            () => PowerSchemeHandler.CreatedSchemes(journal.Load().Journal),
            () => PowerSchemeHandler.FormatKitSchemes(journal.Load().Journal));
        var applier = new ItemApplier(handlers, journal);
        var context = new StepContext(services, log);
        return RunAsync(catalog, item => applier.IsAppliedAsync(item, context, ct), log, ct);
    }

    internal static async Task<CaptureSection> RunAsync(
        CleanupCatalog catalog, Func<CleanupItem, Task<bool>> isApplied, Serilog.ILogger log, CancellationToken ct)
    {
        var include = new List<string>();
        var aggressive = new List<string>();
        var contested = new List<string>();
        var unchecked_ = new List<string>();

        foreach (var item in catalog.Items)
        {
            ct.ThrowIfCancellationRequested();
            bool applied;
            try
            {
                applied = await isApplied(item).ConfigureAwait(false);
            }
            catch (TargetNotPresentException)
            {
                // This Windows does not have what the item changes, so there is nothing to carry over.
                continue;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.Debug(ex, "Could not check cleanup item {Item}", item.Id);
                unchecked_.Add(item.Id);
                continue;
            }

            if (!applied)
                continue;

            if (item.Level == CleanupLevel.Aggressive)
            {
                aggressive.Add(item.Id);
                continue;
            }

            include.Add(item.Id);
            if (item.Contested)
                contested.Add(item.Id);
        }

        var notes = new List<CaptureNote>();
        if (aggressive.Count > 0)
            notes.Add(Note(CaptureNoteKind.Review, "Capture_Aggressive", aggressive));
        if (contested.Count > 0)
            notes.Add(Note(CaptureNoteKind.Review, "Capture_Contested", contested));
        if (unchecked_.Count > 0)
            notes.Add(Note(CaptureNoteKind.NotCaptured, "Capture_NotChecked", unchecked_));

        var value = new JsonObject { ["enabled"] = include.Count > 0 };
        if (include.Count > 0)
            value["include"] = new JsonArray(include.Select(id => (JsonNode)JsonValue.Create(id)).ToArray());

        return new CaptureSection(value, notes, Strings.Format("Capture_Summary", include.Count));
    }

    private static CaptureNote Note(CaptureNoteKind kind, string text, List<string> ids) =>
        new(kind, $"{SectionPath}.include", Strings.Format(text, string.Join(", ", ids)));
}
