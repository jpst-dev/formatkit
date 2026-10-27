using System.Text.Json;

namespace FormatKit.Cleanup.Journal;

/// <summary>One reversible change. <paramref name="Target"/> is a stable key: registry "path|name", service name, appx package, "power", feature name, or named handler.</summary>
public sealed record JournalAction(string Type, string Target, JsonElement Previous);

public sealed record JournalEntry(string ItemId, DateTimeOffset AppliedAt, List<JournalAction> Actions, DateTimeOffset? RevertedAt);

public sealed record CleanupJournal(int SchemaVersion, List<JournalEntry> Entries);

public enum JournalLoadStatus { None, Loaded, Corrupt }

public sealed record JournalLoadResult(JournalLoadStatus Status, CleanupJournal? Journal, string? Error);

public sealed class JournalCorruptException(string path, string error)
    : Exception($"Cleanup journal '{path}' is corrupt: {error}")
{
    public string Path { get; } = path;
    public string Error { get; } = error;
}
