using System.Text.Json.Nodes;
using FormatKit.Core.Capture;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Programs.Catalog;
using FormatKit.Programs.Localization;

namespace FormatKit.Programs.Capture;

/// <summary>Captures the installed programs from <c>winget export</c>, leaving every agreement to the user.</summary>
public static class ProgramsCapture
{
    private const string SectionPath = "modules.programs";

    public static async Task<CaptureSection> RunAsync(
        ServiceSet services, ProgramCatalog catalog, string tempDirectory, Serilog.ILogger log, CancellationToken ct)
    {
        var temp = Path.Combine(tempDirectory, $"formatkit-capture-{Guid.NewGuid():N}.json");
        try
        {
            var result = await services.Winget.ExportAsync(temp, ct, acceptSourceAgreements: false).ConfigureAwait(false);
            switch (result.Status)
            {
                case WingetStatus.WingetMissing:
                    return Disabled(Strings.Get("Capture_WingetMissing"));
                case WingetStatus.Failed:
                    log.Warning("winget export failed with exit code {Exit}: {Output}", result.ExitCode, result.Output);
                    return Disabled(Strings.Format("Capture_ExportFailed", result.ExitCode));
            }

            return FromExport(services.Files.ReadAllText(temp), catalog, log);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            log.Warning(ex, "The winget export could not be read");
            return Disabled(Strings.Format("Capture_ExportUnreadable", ex.Message));
        }
        finally
        {
            DeleteQuietly(services.Files, temp, log);
        }
    }

    private static CaptureSection FromExport(string json, ProgramCatalog catalog, Serilog.ILogger log)
    {
        var bySource = catalog.Programs.ToDictionary(p => (p.WingetId.ToLowerInvariant(), p.Source), p => p.Id);
        var ids = new List<string>();
        var winget = new List<(string Id, string Source)>();
        var seen = new HashSet<(string, string)>();
        var skipped = 0;

        foreach (var entry in WingetImportFile.Read(json, warning => log.Information("winget export: {Warning}", warning)))
        {
            if (!seen.Add((entry.Id.ToLowerInvariant(), entry.Source)))
                continue;

            if (SkippedPackages.IsSkipped(entry.Id))
                skipped++;
            else if (bySource.TryGetValue((entry.Id.ToLowerInvariant(), entry.Source), out var catalogId))
                ids.Add(catalogId);
            else
                winget.Add((entry.Id, entry.Source));
        }

        var summary = Strings.Format("Capture_Summary", ids.Count, winget.Count, skipped);
        if (ids.Count + winget.Count == 0)
            return new CaptureSection(new JsonObject { ["enabled"] = false }, [], summary);

        var value = new JsonObject { ["enabled"] = true, ["acceptAgreements"] = false };
        if (ids.Count > 0)
            value["catalog"] = new JsonArray(ids.Select(id => (JsonNode)JsonValue.Create(id)).ToArray());
        if (winget.Count > 0)
            value["winget"] = new JsonArray(winget
                .Select(w => (JsonNode)new JsonObject { ["id"] = w.Id, ["source"] = w.Source })
                .ToArray());

        return new CaptureSection(
            value,
            [new CaptureNote(CaptureNoteKind.Review, $"{SectionPath}.acceptAgreements", Strings.Get("Capture_AcceptAgreements"))],
            summary);
    }

    private static CaptureSection Disabled(string reason) => new(
        new JsonObject { ["enabled"] = false },
        [new CaptureNote(CaptureNoteKind.NotCaptured, SectionPath, reason)],
        Strings.Get("Capture_SummaryNone"));

    private static void DeleteQuietly(IFileSystem files, string path, Serilog.ILogger log)
    {
        try
        {
            files.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warning(ex, "The temporary export {Path} could not be deleted", path);
        }
    }
}
