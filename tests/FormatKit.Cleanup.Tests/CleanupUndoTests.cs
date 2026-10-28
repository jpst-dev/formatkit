using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Core;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using static FormatKit.Cleanup.Tests.TestItems;

namespace FormatKit.Cleanup.Tests;

public class CleanupUndoTests
{
    private const string Key = @"HKLM\Software\Test";
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly InMemoryFileSystem _files = new();
    private readonly AppPaths _paths = new(@"C:\FormatKitTest");
    private readonly JournalStore _journal;

    public CleanupUndoTests() => _journal = new JournalStore(_files, _paths);

    private StepContext Context(FakeRegistry registry) =>
        TestServices.Context(TestServices.Create(files: _files, registry: registry));

    private Task<ItemOutcome> Apply(CleanupItem item, FakeRegistry registry) =>
        new ItemApplier(new ActionHandlers(() => new Dictionary<Guid, Guid?>()), _journal).ApplyAsync(item, Context(registry), Ct);

    private Task<IReadOnlyList<ItemOutcome>> Undo(FakeRegistry registry, IReadOnlyList<string>? ids = null) =>
        new CleanupUndo(NewCatalog(), _paths, _files).UndoAsync(ids, Context(registry), Ct);

    [Fact]
    public async Task Undo_all_restores_registry_to_original()
    {
        var registry = new FakeRegistry();
        registry.Set(Key, "A", RegistryKind.Dword, "1");
        registry.Set(Key, "B", RegistryKind.String, "x");
        await Apply(Item("privacy.a", actions: [Dword(Key, "A", 0), Dword(Key, "C", 0)]), registry);
        await Apply(Item("privacy.b", actions: [Dword(Key, "B", 5)]), registry);
        Assert.Equal("0", registry.Read(Key, "A")!.Data);

        var outcomes = await Undo(registry);

        Assert.All(outcomes, o => Assert.Equal(ItemStatus.Applied, o.Status));
        Assert.Equal("1", registry.Read(Key, "A")!.Data);
        Assert.Equal(RegistryKind.String, registry.Read(Key, "B")!.Kind);
        Assert.Equal("x", registry.Read(Key, "B")!.Data);
        Assert.Null(registry.Read(Key, "C"));
        Assert.All(_journal.Load().Journal!.Entries, e => Assert.NotNull(e.RevertedAt));
    }

    [Fact]
    public async Task Applying_twice_then_undo_once_returns_to_original()
    {
        var registry = new FakeRegistry();
        registry.Set(Key, "A", RegistryKind.Dword, "1");
        var item = Item("privacy.a", actions: [Dword(Key, "A", 0)]);
        await Apply(item, registry);
        registry.Set(Key, "A", RegistryKind.Dword, "7"); // external change
        await Apply(item, registry);
        Assert.Equal(2, _journal.Load().Journal!.Entries.Count);

        var outcomes = await Undo(registry, ["privacy.a"]);

        Assert.Equal(ItemStatus.Applied, Assert.Single(outcomes).Status);
        Assert.Equal("1", registry.Read(Key, "A")!.Data);
        Assert.All(_journal.Load().Journal!.Entries, e => Assert.NotNull(e.RevertedAt));
    }

    [Fact]
    public async Task Entry_with_previous_equal_current_undoes_harmlessly()
    {
        var registry = new FakeRegistry();
        registry.Set(Key, "A", RegistryKind.Dword, "1");
        await Apply(Item("privacy.a", actions: [Dword(Key, "A", 0)]), registry);
        registry.Set(Key, "A", RegistryKind.Dword, "1"); // already back at the previous value

        var outcomes = await Undo(registry);

        Assert.Equal(ItemStatus.Applied, Assert.Single(outcomes).Status);
        Assert.Equal("1", registry.Read(Key, "A")!.Data);
    }

    [Fact]
    public async Task Unknown_id_is_skipped()
    {
        var registry = new FakeRegistry();

        var outcomes = await Undo(registry, ["privacy.nope"]);

        var outcome = Assert.Single(outcomes);
        Assert.Equal(ItemStatus.Skipped, outcome.Status);
        Assert.Equal(Localization.Strings.Get("Undo_NothingToUndo"), outcome.Message);
    }

    [Fact]
    public async Task Already_reverted_item_is_skipped()
    {
        var registry = new FakeRegistry();
        registry.Set(Key, "A", RegistryKind.Dword, "1");
        await Apply(Item("privacy.a", actions: [Dword(Key, "A", 0)]), registry);
        await Undo(registry);

        var outcomes = await Undo(registry, ["privacy.a"]);

        Assert.Equal(ItemStatus.Skipped, Assert.Single(outcomes).Status);
    }

    [Fact]
    public async Task Failed_target_leaves_entries_unreverted()
    {
        var registry = new RefusingRegistry();
        registry.Set(Key, "A", RegistryKind.Dword, "1");
        registry.Set(Key, "B", RegistryKind.Dword, "1");
        await Apply(Item("privacy.a", actions: [Dword(Key, "A", 0), Dword(Key, "B", 0)]), registry);
        registry.RefuseWritesTo = "B";

        var outcomes = await Undo(registry);

        var outcome = Assert.Single(outcomes);
        Assert.Equal(ItemStatus.Failed, outcome.Status);
        Assert.Contains("write refused", outcome.Message);
        Assert.Null(Assert.Single(_journal.Load().Journal!.Entries).RevertedAt);

        registry.RefuseWritesTo = null;
        Assert.Equal(ItemStatus.Applied, Assert.Single(await Undo(registry)).Status);
        Assert.Equal("1", registry.Read(Key, "B")!.Data);
    }

    [Fact]
    public async Task Newest_item_undone_first()
    {
        var registry = new FakeRegistry();
        registry.Set(Key, "A", RegistryKind.Dword, "1");
        registry.Set(Key, "B", RegistryKind.Dword, "1");
        await Apply(Item("privacy.old", actions: [Dword(Key, "A", 0)]), registry);
        await Task.Delay(20, Ct);
        await Apply(Item("privacy.new", actions: [Dword(Key, "B", 0)]), registry);

        var outcomes = await Undo(registry);

        Assert.Equal(["privacy.new", "privacy.old"], outcomes.Select(o => o.ItemId));
    }

    [Fact]
    public async Task Corrupt_journal_throws()
    {
        _files.Seed(_journal.Path, "{ not json");

        var ex = await Assert.ThrowsAsync<JournalCorruptException>(() => Undo(new FakeRegistry()));

        Assert.Equal(_journal.Path, ex.Path);
    }

    private sealed class RefusingRegistry : FakeRegistry
    {
        public string? RefuseWritesTo { get; set; }

        public override void Write(string path, string name, RegistryValue value)
        {
            if (name == RefuseWritesTo)
                throw new InvalidOperationException("write refused");
            base.Write(path, name, value);
        }
    }
}
