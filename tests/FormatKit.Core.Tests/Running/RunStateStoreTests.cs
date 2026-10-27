using FormatKit.Core.Running;
using FormatKit.Core.Steps;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.Running;

public class RunStateStoreTests
{
    private static readonly AppPaths Paths = new(@"C:\FormatKitTest");

    private static RunState Sample() => new(
        @"C:\profiles\gamer.json",
        "abc123",
        ["a", "b", "c"],
        new Dictionary<string, StepRecord>
        {
            ["a"] = new(StepOutcome.Succeeded, new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero), ["ok"], null),
            ["b"] = new(StepOutcome.Failed, new DateTimeOffset(2026, 10, 27, 10, 5, 0, TimeSpan.Zero), ["boom", "bang"], "retry"),
        },
        new DateTimeOffset(2026, 10, 27, 9, 59, 0, TimeSpan.Zero));

    [Fact]
    public void Load_without_file_returns_None()
    {
        var result = new RunStateStore(new InMemoryFileSystem(), Paths).Load();

        Assert.Equal(StateLoadStatus.None, result.Status);
        Assert.Null(result.State);
    }

    [Fact]
    public void Save_then_Load_round_trips()
    {
        var fs = new InMemoryFileSystem();
        var store = new RunStateStore(fs, Paths);
        var state = Sample();

        store.Save(state);
        var result = store.Load();

        Assert.Equal(StateLoadStatus.Loaded, result.Status);
        var loaded = result.State!;
        Assert.Equal(state.ProfilePath, loaded.ProfilePath);
        Assert.Equal(state.ProfileHash, loaded.ProfileHash);
        Assert.Equal(state.PlanStepIds, loaded.PlanStepIds);
        Assert.Equal(state.StartedAt, loaded.StartedAt);
        Assert.Equal(state.Results.Keys.Order(), loaded.Results.Keys.Order());
        foreach (var (id, expected) in state.Results)
        {
            var actual = loaded.Results[id];
            Assert.Equal(expected.Outcome, actual.Outcome);
            Assert.Equal(expected.FinishedAt, actual.FinishedAt);
            Assert.Equal(expected.Messages, actual.Messages);
            Assert.Equal(expected.NextAction, actual.NextAction);
        }

        var json = fs.ReadAllText(Paths.StateFile);
        Assert.Contains("\"Failed\"", json);
        Assert.Contains("\"profilePath\"", json);
    }

    [Fact]
    public void Corrupt_json_returns_Corrupt_with_message()
    {
        var fs = new InMemoryFileSystem();
        fs.Seed(Paths.StateFile, "{\"profilePath\": ");

        var result = new RunStateStore(fs, Paths).Load();

        Assert.Equal(StateLoadStatus.Corrupt, result.Status);
        Assert.Null(result.State);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Wrong_shape_returns_Corrupt()
    {
        var fs = new InMemoryFileSystem();
        fs.Seed(Paths.StateFile, "[]");

        var result = new RunStateStore(fs, Paths).Load();

        Assert.Equal(StateLoadStatus.Corrupt, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Delete_removes_state()
    {
        var fs = new InMemoryFileSystem();
        var store = new RunStateStore(fs, Paths);
        store.Save(Sample());

        store.Delete();

        Assert.False(fs.FileExists(Paths.StateFile));
        Assert.Equal(StateLoadStatus.None, store.Load().Status);
    }

    [Fact]
    public void Delete_without_file_is_noop()
    {
        new RunStateStore(new InMemoryFileSystem(), Paths).Delete();
    }

    [Fact]
    public void Save_creates_missing_parent_directory()
    {
        var fs = new InMemoryFileSystem();

        new RunStateStore(fs, Paths).Save(Sample());

        Assert.True(fs.DirectoryExists(Paths.Root));
        Assert.Equal(1, fs.WriteCount);
    }

    [Fact]
    public void AppPaths_derive_from_root()
    {
        Assert.Equal(@"C:\FormatKitTest\state.json", Paths.StateFile);
        Assert.Equal(@"C:\FormatKitTest\logs", Paths.LogsDirectory);
        Assert.Equal(@"C:\FormatKitTest\reports", Paths.ReportsDirectory);
        Assert.EndsWith("FormatKit", AppPaths.Default.Root);
    }

    private const string Head = "\"profilePath\":\"p\",\"profileHash\":\"h\",\"startedAt\":\"2026-10-27T10:00:00+00:00\"";

    [Theory]
    [InlineData("{" + Head + ",\"planStepIds\":[\"a\"],\"results\":{\"a\":null}}")]
    [InlineData("{" + Head + ",\"planStepIds\":[\"a\"],\"results\":{\"a\":{\"outcome\":\"Failed\",\"finishedAt\":\"2026-10-27T10:00:00+00:00\"}}}")]
    [InlineData("{" + Head + ",\"planStepIds\":[null],\"results\":{}}")]
    [InlineData("{" + Head + ",\"planStepIds\":[\"a\"],\"results\":{\"a\":{\"outcome\":99,\"finishedAt\":\"2026-10-27T10:00:00+00:00\",\"messages\":[]}}}")]
    public void Null_entries_or_integer_outcome_return_Corrupt(string json)
    {
        var fs = new InMemoryFileSystem();
        fs.Seed(Paths.StateFile, json);

        var result = new RunStateStore(fs, Paths).Load();

        Assert.Equal(StateLoadStatus.Corrupt, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void Read_failure_returns_Corrupt(Type exceptionType)
    {
        var fs = new ThrowingReadFileSystem((Exception)Activator.CreateInstance(exceptionType, "locked")!);

        var result = new RunStateStore(fs, Paths).Load();

        Assert.Equal(StateLoadStatus.Corrupt, result.Status);
        Assert.Equal("locked", result.Error);
    }

    private sealed class ThrowingReadFileSystem(Exception toThrow) : FormatKit.Core.SystemAccess.IFileSystem
    {
        public bool FileExists(string path) => true;
        public bool DirectoryExists(string path) => true;
        public string ReadAllText(string path) => throw toThrow;
        public void WriteAllTextAtomic(string path, string contents) { }
        public void Delete(string path) { }
        public void CreateDirectory(string path) { }
        public IEnumerable<string> EnumerateFiles(string dir, string pattern, bool recursive) => [];
    }

    [Fact]
    public void Step_record_without_outcome_returns_Corrupt()
    {
        var fs = new InMemoryFileSystem();
        fs.Seed(Paths.StateFile,
            "{" + Head + ",\"planStepIds\":[\"a\"],\"results\":{\"a\":{\"finishedAt\":\"2026-10-27T10:00:00+00:00\",\"messages\":[]}}}");

        var result = new RunStateStore(fs, Paths).Load();

        Assert.Equal(StateLoadStatus.Corrupt, result.Status);
    }
}
