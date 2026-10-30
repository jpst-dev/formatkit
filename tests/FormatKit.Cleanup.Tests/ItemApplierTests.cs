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

    private ItemApplier NewApplier() => new(new ActionHandlers(() => new Dictionary<Guid, Guid?>()), _journal);

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
    public async Task Hkcu_item_records_the_user_sid_and_hklm_item_does_not()
    {
        var registry = new FakeRegistry();
        var session = new FakeUserSession { CurrentUserSid = "S-1-5-21-1-2-3-1001" };
        var ctx = TestServices.Context(TestServices.Create(files: _files, registry: registry, session: session));

        await NewApplier().ApplyAsync(Item("privacy.user", actions: [Dword(Key, "A", 0), Dword(@"HKCU\Software\Test", "B", 0)]), ctx, Ct);
        await NewApplier().ApplyAsync(Item("privacy.machine", actions: [Dword(Key, "C", 0)]), ctx, Ct);

        var entries = _journal.Load().Journal!.Entries;
        Assert.Equal("S-1-5-21-1-2-3-1001", entries.Single(e => e.ItemId == "privacy.user").UserSid);
        Assert.Null(entries.Single(e => e.ItemId == "privacy.machine").UserSid);
    }

    [Fact]
    public async Task User_scope_startup_item_records_the_user_sid_and_machine_scope_does_not()
    {
        var registry = new FakeRegistry();
        registry.Set(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "App", RegistryKind.String, "app.exe");
        registry.Set(@"HKLM\Software\Microsoft\Windows\CurrentVersion\Run", "App", RegistryKind.String, "app.exe");
        var session = new FakeUserSession { CurrentUserSid = "S-1-5-21-1-2-3-1001" };
        var ctx = TestServices.Context(TestServices.Create(files: _files, registry: registry, session: session));

        await NewApplier().ApplyAsync(Item("startup.user", scope: ItemScope.User, actions: [new StartupItemAction("App", ItemScope.User)]), ctx, Ct);
        await NewApplier().ApplyAsync(Item("startup.machine", actions: [new StartupItemAction("App", ItemScope.Machine)]), ctx, Ct);

        var entries = _journal.Load().Journal!.Entries;
        Assert.Equal("S-1-5-21-1-2-3-1001", entries.Single(e => e.ItemId == "startup.user").UserSid);
        Assert.Null(entries.Single(e => e.ItemId == "startup.machine").UserSid);
    }

    [Fact]
    public async Task Startup_item_with_missing_run_value_is_already_in_place_and_journals_nothing()
    {
        var outcome = await NewApplier().ApplyAsync(
            Item("startup.user", scope: ItemScope.User, actions: [new StartupItemAction("Gone", ItemScope.User)]),
            Context(new FakeRegistry()), Ct);

        Assert.Equal(ItemStatus.AlreadyInPlace, outcome.Status);
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
        var registry = new FailingRegistry((name, attempt) => name == "B" && attempt == 1);
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
    public async Task Failing_action_is_rolled_back_after_verify_mismatch()
    {
        var registry = new MismatchingRegistry();
        registry.Set(Key, "A", RegistryKind.Dword, "1");

        var outcome = await NewApplier().ApplyAsync(Item("privacy.a", actions: [Dword(Key, "A", 0)]), Context(registry), Ct);

        Assert.Equal(ItemStatus.Failed, outcome.Status);
        Assert.Equal("1", registry.Read(Key, "A")!.Data);
        Assert.NotNull(Assert.Single(_journal.Load().Journal!.Entries).RevertedAt);
    }

    [Fact]
    public async Task Journal_save_failure_after_change_rolls_back_and_fails()
    {
        var registry = new FakeRegistry();
        registry.Set(Key, "A", RegistryKind.Dword, "1");
        var files = new FailingFileSystem(_files, failingWrite: 2);
        var applier = new ItemApplier(new ActionHandlers(() => new Dictionary<Guid, Guid?>()), new JournalStore(files, new AppPaths(@"C:\FormatKitTest")));

        var outcome = await applier.ApplyAsync(Item("privacy.a", actions: [Dword(Key, "A", 0)]), Context(registry), Ct);

        Assert.Equal(ItemStatus.Failed, outcome.Status);
        Assert.Contains("disk full", outcome.Message);
        Assert.Equal("1", registry.Read(Key, "A")!.Data);
    }

    [Fact]
    public async Task Incomplete_rollback_leaves_entry_unreverted()
    {
        var registry = new FailingRegistry((name, attempt) => (name == "B" && attempt == 1) || (name == "A" && attempt == 2));
        registry.Set(Key, "A", RegistryKind.Dword, "1");
        registry.Set(Key, "B", RegistryKind.Dword, "1");
        var item = Item("privacy.a", actions: [Dword(Key, "A", 0), Dword(Key, "B", 0)]);

        var outcome = await NewApplier().ApplyAsync(item, Context(registry), Ct);

        Assert.Equal(ItemStatus.Failed, outcome.Status);
        Assert.Null(Assert.Single(_journal.Load().Journal!.Entries).RevertedAt);
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

    /// <summary>Throws on the writes <paramref name="fails"/> selects by value name and 1-based write count for that name.</summary>
    private sealed class FailingRegistry(Func<string, int, bool> fails) : FakeRegistry
    {
        private readonly Dictionary<string, int> _writes = [];

        public override void Write(string path, string name, RegistryValue value)
        {
            _writes[name] = _writes.GetValueOrDefault(name) + 1;
            if (fails(name, _writes[name]))
                throw new InvalidOperationException("write refused");
            base.Write(path, name, value);
        }
    }

    /// <summary>The first write of each value stores different data, so the handler's verify fails after the change.</summary>
    private sealed class MismatchingRegistry : FakeRegistry
    {
        private readonly HashSet<string> _corrupted = [];

        public override void Write(string path, string name, RegistryValue value) =>
            base.Write(path, name, _corrupted.Add(name) ? value with { Data = "5" } : value);
    }

    /// <summary>Delegates to an in-memory file system and throws on the selected (1-based) write.</summary>
    private sealed class FailingFileSystem(InMemoryFileSystem inner, int failingWrite) : IFileSystem
    {
        private int _writes;

        public bool FileExists(string path) => inner.FileExists(path);
        public bool DirectoryExists(string path) => inner.DirectoryExists(path);
        public string ReadAllText(string path) => inner.ReadAllText(path);
        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);
        public void Copy(string source, string destination) => inner.Copy(source, destination);
        public void Move(string source, string destination) => inner.Move(source, destination);
        public void Delete(string path) => inner.Delete(path);
        public void CreateDirectory(string path) => inner.CreateDirectory(path);
        public Stream OpenRead(string path) => inner.OpenRead(path);
        public IEnumerable<string> EnumerateFiles(string dir, string pattern, bool recursive) => inner.EnumerateFiles(dir, pattern, recursive);
        public IEnumerable<string> EnumerateDirectories(string dir, Action<string>? onUnreadable = null) => throw new NotSupportedException();
        public IEnumerable<FileEntry> EnumerateFileEntries(string dir, bool recursive, Action<string>? onUnreadable = null) => throw new NotSupportedException();

        public void WriteAllBytes(string path, byte[] bytes) => inner.WriteAllBytes(path, bytes);

        public void WriteAllTextAtomic(string path, string contents)
        {
            if (++_writes == failingWrite)
                throw new IOException("disk full");
            inner.WriteAllTextAtomic(path, contents);
        }
    }
}
