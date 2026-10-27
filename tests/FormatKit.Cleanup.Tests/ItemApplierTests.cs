using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Core;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using static FormatKit.Cleanup.Tests.TestItems;

namespace FormatKit.Cleanup.Tests;

public class ItemApplierTests
{
    private const string Key = @"HKLM\Software\Test";
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly InMemoryFileSystem _files = new();
    private readonly JournalStore _journal;

    public ItemApplierTests() => _journal = new JournalStore(_files, new AppPaths(@"C:\FormatKitTest"));

    private ItemApplier NewApplier() => new(new ActionHandlers(() => new HashSet<Guid>()), _journal);

    private StepContext Context(FakeRegistry registry, FakeOptionalFeatures? features = null) =>
        TestServices.Context(TestServices.Create(files: _files, registry: registry, features: features));

    [Fact]
    public async Task All_applied_returns_AlreadyInPlace_and_writes_no_entry()
    {
        var registry = new FakeRegistry();
        registry.Set(Key, "A", RegistryKind.Dword, "0");

        var outcome = await NewApplier().ApplyAsync(Item("privacy.a", actions: [Dword(Key, "A", 0)]), Context(registry), Ct);

        Assert.Equal(ItemStatus.AlreadyInPlace, outcome.Status);
        Assert.Equal("privacy.a", outcome.ItemId);
        Assert.False(_files.FileExists(_journal.Path));
    }

    [Fact]
    public async Task Applies_only_unapplied_actions_and_journals_them()
    {
        var registry = new FakeRegistry();
        registry.Set(Key, "A", RegistryKind.Dword, "0");
        registry.Set(Key, "B", RegistryKind.Dword, "1");
        var item = Item("privacy.a", actions: [Dword(Key, "A", 0), Dword(Key, "B", 0)]);

        var outcome = await NewApplier().ApplyAsync(item, Context(registry), Ct);

        Assert.Equal(ItemStatus.Applied, outcome.Status);
        Assert.Equal("0", registry.Read(Key, "B")!.Data);
        var entry = Assert.Single(_journal.Load().Journal!.Entries);
        Assert.Equal("privacy.a", entry.ItemId);
        Assert.Null(entry.RevertedAt);
        var action = Assert.Single(entry.Actions);
        Assert.Equal($"{Key}|B", action.Target);
        Assert.Equal("1", action.Previous.GetProperty("data").GetString());
    }

    [Fact]
    public async Task Journal_is_written_before_change()
    {
        var registry = new JournalCheckingRegistry(_files, _journal.Path);
        registry.Set(Key, "A", RegistryKind.Dword, "1");

        var outcome = await NewApplier().ApplyAsync(Item("privacy.a", actions: [Dword(Key, "A", 0)]), Context(registry), Ct);

        Assert.Equal(ItemStatus.Applied, outcome.Status);
        Assert.True(registry.SawCapture);
    }

    [Fact]
    public async Task Failure_rolls_back_earlier_actions_and_marks_entry_reverted()
    {
        var registry = new FailingRegistry("B");
        registry.Set(Key, "A", RegistryKind.Dword, "1");
        registry.Set(Key, "B", RegistryKind.Dword, "1");
        var item = Item("privacy.a", actions: [Dword(Key, "A", 0), Dword(Key, "B", 0)]);

        var outcome = await NewApplier().ApplyAsync(item, Context(registry), Ct);

        Assert.Equal(ItemStatus.Failed, outcome.Status);
        Assert.Contains("write refused", outcome.Message);
        Assert.Equal("1", registry.Read(Key, "A")!.Data);
        var entry = Assert.Single(_journal.Load().Journal!.Entries);
        Assert.NotNull(entry.RevertedAt);
    }

    [Fact]
    public async Task Missing_target_is_skipped_not_failed()
    {
        var item = Item("performance.feature", actions: [new OptionalFeatureAction("Missing", OptionalFeatureState.Enabled)]);

        var outcome = await NewApplier().ApplyAsync(item, Context(new FakeRegistry(), new FakeOptionalFeatures()), Ct);

        Assert.Equal(ItemStatus.Skipped, outcome.Status);
        Assert.Equal(Localization.Strings.Get("Skip_NotPresent"), outcome.Message);
        Assert.Empty(_journal.Load().Journal?.Entries ?? []);
    }

    [Fact]
    public async Task Corrupt_journal_throws()
    {
        _files.Seed(_journal.Path, "{ not json");
        var registry = new FakeRegistry();

        var ex = await Assert.ThrowsAsync<JournalCorruptException>(
            () => NewApplier().ApplyAsync(Item("privacy.a", actions: [Dword(Key, "A", 0)]), Context(registry), Ct));

        Assert.Equal(_journal.Path, ex.Path);
        Assert.Null(registry.Read(Key, "A"));
    }

    /// <summary>Records whether the journal on disk already held this value's capture when the write happened.</summary>
    private sealed class JournalCheckingRegistry(InMemoryFileSystem files, string journalPath) : FakeRegistry
    {
        public bool SawCapture { get; private set; }

        public override void Write(string path, string name, RegistryValue value)
        {
            SawCapture = files.FileExists(journalPath) && files.ReadAllText(journalPath).Contains($"|{name}\"", StringComparison.Ordinal);
            base.Write(path, name, value);
        }
    }

    private sealed class FailingRegistry(string failingName) : FakeRegistry
    {
        public override void Write(string path, string name, RegistryValue value)
        {
            if (name == failingName)
                throw new InvalidOperationException("write refused");
            base.Write(path, name, value);
        }
    }
}
