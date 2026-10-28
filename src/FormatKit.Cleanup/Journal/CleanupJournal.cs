using System.Text.Json;

namespace FormatKit.Cleanup.Journal;

/// <summary>One reversible change. <paramref name="Target"/> is a stable key: registry "path|name", service name, appx package, "power", feature name, or named handler.</summary>
public sealed record JournalAction(string Type, string Target, JsonElement Previous);

/// <param name="UserSid">The user who applied an item with HKCU actions (R15); only that user can undo it. Null for machine-wide items and older journals.</param>
public sealed record JournalEntry(string ItemId, DateTimeOffset AppliedAt, List<JournalAction> Actions, DateTimeOffset? RevertedAt, string? UserSid = null)
{
    /// <summary>Whether <paramref name="currentSid"/> may undo this entry: it records no user, or the same one.</summary>
    public bool CanBeUndoneBy(string? currentSid) =>
        UserSid is null || string.Equals(UserSid, currentSid, StringComparison.OrdinalIgnoreCase);
}

public sealed record CleanupJournal(int SchemaVersion, List<JournalEntry> Entries);

public enum JournalLoadStatus { None, Loaded, Corrupt }

public sealed record JournalLoadResult(JournalLoadStatus Status, CleanupJournal? Journal, string? Error);

public sealed class JournalCorruptException(string path, string error)
    : Exception($"Cleanup journal '{path}' is corrupt: {error}")
{
    public string Path { get; } = path;
    public string Error { get; } = error;
}
