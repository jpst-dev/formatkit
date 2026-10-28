using System.Text.Json;
using FormatKit.Core.SystemAccess;
using FormatKit.Programs.Catalog;
using FormatKit.Programs.Localization;

namespace FormatKit.Programs;

/// <summary>Reads the JSON that <c>winget export</c> writes (the only winget output FormatKit parses).</summary>
public static class WingetImportFile
{
    public static IReadOnlyList<WingetEntry> Read(string json, Action<string> warn)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(Strings.Format("Import_InvalidJson", ex.Message), ex);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("Sources", out var sources)
                || sources.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException(Strings.Get("Import_NoSources"));

            var entries = new List<WingetEntry>();
            foreach (var source in sources.EnumerateArray())
            {
                if (source.ValueKind != JsonValueKind.Object)
                    continue;

                var name = source.TryGetProperty("SourceDetails", out var details) && details.ValueKind == JsonValueKind.Object
                    && details.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString()! : "";
                var mapped = name.ToLowerInvariant() switch
                {
                    "winget" => "winget",
                    "msstore" => "msstore",
                    _ => null,
                };
                if (mapped is null)
                {
                    warn(Strings.Format("Import_SkippedSource", name));
                    continue;
                }

                if (!source.TryGetProperty("Packages", out var packages) || packages.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var package in packages.EnumerateArray())
                {
                    if (package.ValueKind != JsonValueKind.Object
                        || !package.TryGetProperty("PackageIdentifier", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                        continue;

                    var id = idEl.GetString()!;
                    try
                    {
                        WingetClient.Validate(id, "id");
                    }
                    catch (ArgumentException)
                    {
                        warn(Strings.Format("Import_SkippedId", id));
                        continue;
                    }

                    entries.Add(new WingetEntry(id, mapped, null, InstallContext.Machine));
                }
            }

            return entries;
        }
    }
}
