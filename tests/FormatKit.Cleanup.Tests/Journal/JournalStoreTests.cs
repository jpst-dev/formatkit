using System.Text.Json;
using FormatKit.Cleanup.Journal;
using FormatKit.Core;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Cleanup.Tests.Journal;

public class JournalStoreTests
{
    private static readonly AppPaths Paths = new(@"C:\FormatKitTest");

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Missing_file_is_None()
    {
        var result = new JournalStore(new InMemoryFileSystem(), Paths).Load();

        Assert.Equal(JournalLoadStatus.None, result.Status);
        Assert.Null(result.Journal);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Round_trip_preserves_entries_and_previous_json()
    {
        var fs = new InMemoryFileSystem();
        var store = new JournalStore(fs, Paths);
        var applied = new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero);
        var reverted = new DateTimeOffset(2026, 10, 27, 11, 0, 0, TimeSpan.Zero);
        var journal = new CleanupJournal(1,
        [
            new JournalEntry("telemetry", applied,
            [
                new JournalAction("registry", @"HKLM\Software\X|Value", Json("{\"kind\":\"dword\",\"data\":1}")),
                new JournalAction("service", "DiagTrack", Json("\"Automatic\"")),
                new JournalAction("power", "power", Json("null")),
            ], reverted),
            new JournalEntry("other", applied, [], null),
        ]);

        store.Save(journal);
        var result = store.Load();

        Assert.Equal(JournalLoadStatus.Loaded, result.Status);
        var loaded = result.Journal!;
        Assert.Equal(1, loaded.SchemaVersion);
        Assert.Equal(2, loaded.Entries.Count);
        var first = loaded.Entries[0];
        Assert.Equal("telemetry", first.ItemId);
        Assert.Equal(applied, first.AppliedAt);
        Assert.Equal(reverted, first.RevertedAt);
        Assert.Equal(3, first.Actions.Count);
        Assert.Equal("registry", first.Actions[0].Type);
        Assert.Equal(@"HKLM\Software\X|Value", first.Actions[0].Target);
        Assert.Equal("dword", first.Actions[0].Previous.GetProperty("kind").GetString());
        Assert.Equal(1, first.Actions[0].Previous.GetProperty("data").GetInt32());
        Assert.Equal("Automatic", first.Actions[1].Previous.GetString());
        Assert.Equal(JsonValueKind.Null, first.Actions[2].Previous.ValueKind);
        Assert.Null(loaded.Entries[1].RevertedAt);
        Assert.Empty(loaded.Entries[1].Actions);
    }

    [Fact]
    public void Save_writes_without_bom_and_creates_directory()
    {
        var fs = new InMemoryFileSystem();
        var store = new JournalStore(fs, Paths);

        store.Save(new CleanupJournal(1, []));

        Assert.True(fs.DirectoryExists(Paths.Root));
        Assert.Equal(Paths.CleanupJournalFile, store.Path);
        Assert.False(fs.ReadAllText(store.Path).StartsWith('\uFEFF'));
    }

    [Fact]
    public void Invalid_json_is_Corrupt_with_error()
    {
        var fs = new InMemoryFileSystem();
        fs.Seed(Paths.CleanupJournalFile, "{\"schemaVersion\": ");

        var result = new JournalStore(fs, Paths).Load();

        Assert.Equal(JournalLoadStatus.Corrupt, result.Status);
        Assert.Null(result.Journal);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Wrong_schema_version_is_Corrupt()
    {
        var fs = new InMemoryFileSystem();
        fs.Seed(Paths.CleanupJournalFile, "{\"schemaVersion\":2,\"entries\":[]}");

        var result = new JournalStore(fs, Paths).Load();

        Assert.Equal(JournalLoadStatus.Corrupt, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"entries\":[null]}")]
    [InlineData("{\"schemaVersion\":1,\"entries\":[{\"itemId\":\"a\",\"appliedAt\":\"2026-10-27T10:00:00+00:00\"}]}")]
    public void Wrong_shape_is_Corrupt(string json)
    {
        var fs = new InMemoryFileSystem();
        fs.Seed(Paths.CleanupJournalFile, json);

        var result = new JournalStore(fs, Paths).Load();

        Assert.Equal(JournalLoadStatus.Corrupt, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Bom_prefixed_file_loads()
    {
        var fs = new InMemoryFileSystem();
        fs.Seed(Paths.CleanupJournalFile, "\uFEFF{\"schemaVersion\":1,\"entries\":[]}");

        var result = new JournalStore(fs, Paths).Load();

        Assert.Equal(JournalLoadStatus.Loaded, result.Status);
        Assert.Empty(result.Journal!.Entries);
    }

    [Fact]
    public void Corrupt_exception_carries_path_and_error()
    {
        var ex = new JournalCorruptException("p.json", "bad");

        Assert.Equal("p.json", ex.Path);
        Assert.Equal("bad", ex.Error);
        Assert.Contains("p.json", ex.Message);
    }
}
