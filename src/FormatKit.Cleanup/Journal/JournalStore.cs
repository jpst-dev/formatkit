using System.Text.Json;
using FormatKit.Core;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Cleanup.Journal;

public sealed class JournalStore(IFileSystem files, AppPaths paths)
{
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Path => paths.CleanupJournalFile;

    public JournalLoadResult Load()
    {
        if (!files.FileExists(Path))
            return new JournalLoadResult(JournalLoadStatus.None, null, null);

        try
        {
            var text = files.ReadAllText(Path).TrimStart('﻿');
            var journal = JsonSerializer.Deserialize<CleanupJournal>(text, Options);
            if (journal is null || journal.Entries is null)
                return Corrupt("Journal file is missing required fields.");
            if (journal.SchemaVersion != CurrentSchemaVersion)
                return Corrupt($"Unsupported journal schema version {journal.SchemaVersion}.");
            if (journal.Entries.Any(e => e is null || e.ItemId is null || e.Actions is null
                    || e.Actions.Any(a => a is null || a.Type is null || a.Target is null)))
                return Corrupt("Journal file contains null or incomplete entries.");
            return new JournalLoadResult(JournalLoadStatus.Loaded, journal, null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Corrupt(ex.Message);
        }
    }

    public void Save(CleanupJournal journal)
    {
        files.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        files.WriteAllTextAtomic(Path, JsonSerializer.Serialize(journal, Options));
    }

    private static JournalLoadResult Corrupt(string error) => new(JournalLoadStatus.Corrupt, null, error);
}
