using System.Globalization;
using FormatKit.Cleanup;
using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Cli.Tests.Fakes;
using FormatKit.Core;
using FormatKit.Core.Running;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Drivers;

namespace FormatKit.Cli.Tests;

[Collection("Culture")]
public sealed class CleanupCommandsTests : IDisposable
{
    private readonly CultureInfo _original = CultureInfo.CurrentUICulture;
    private readonly string _tempDir = Directory.CreateTempSubdirectory("formatkit-cli-cleanup-").FullName;
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeElevation _elevation = new() { IsElevated = true };
    private readonly FakeServiceControl _services = new();
    private FakeRegistry _registry = new();
    private readonly StringWriter _out = new();
    private readonly CleanupCatalog _catalog = CleanupCatalog.LoadEmbedded();

    public CleanupCommandsTests() => CultureInfo.CurrentUICulture = new CultureInfo("en-US");

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _original;
        Directory.Delete(_tempDir, recursive: true);
    }

    private CliApp App()
    {
        var services = TestServices.Create(files: _fs, services: _services, registry: _registry);
        var env = new CliEnvironment(
            services, _elevation, new FakeScheduledTasks(), new FakeRebooter(), new FakeReportWriter(),
            new RunStateStore(_fs, new AppPaths("data")), Serilog.Core.Logger.None, _out, TimeSpan.Zero, TimeSpan.Zero);
        return new CliApp(env);
    }

    private Task<int> Run(params string[] args) => App().RunAsync(args, CancellationToken.None);

    [Fact]
    public async Task List_prints_every_item()
    {
        var exit = await Run("cleanup", "list");

        Assert.Equal(ExitCodes.Ok, exit);
        var lines = _out.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(_catalog.Items.Count, lines.Length);
        foreach (var item in _catalog.Items)
            Assert.Contains(lines, l => l.StartsWith(item.Id + " ", StringComparison.Ordinal) && l.EndsWith(item.LocalizedText().Title, StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("performance.system-responsiveness ", StringComparison.Ordinal) && l.Contains("[contested]"));
    }

    [Fact]
    public async Task List_filters_by_level()
    {
        var exit = await Run("cleanup", "list", "--level", "safe");

        Assert.Equal(ExitCodes.Ok, exit);
        var output = _out.ToString();
        foreach (var item in _catalog.Items)
            Assert.Equal(item.Level == CleanupLevel.Safe, output.Contains(item.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task List_filters_by_category()
    {
        var exit = await Run("cleanup", "list", "--category", "apps");

        Assert.Equal(ExitCodes.Ok, exit);
        var output = _out.ToString();
        foreach (var item in _catalog.Items)
            Assert.Equal(item.Category == CleanupCategory.Apps, output.Contains(item.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task List_verbose_adds_what_breaks_and_gain()
    {
        var exit = await Run("cleanup", "list", "--category", "apps", "--verbose");

        Assert.Equal(ExitCodes.Ok, exit);
        var text = _catalog.Items.First(i => i.Category == CleanupCategory.Apps).LocalizedText();
        Assert.Contains("    What: " + text.What, _out.ToString());
        Assert.Contains("    Breaks: " + text.Breaks, _out.ToString());
        Assert.Contains("    Gain: " + text.Gain, _out.ToString());
    }

    [Fact]
    public async Task List_invalid_level_exits_2()
    {
        var exit = await Run("cleanup", "list", "--level", "reckless");

        Assert.Equal(ExitCodes.InvalidInput, exit);
    }

    [Fact]
    public async Task List_invalid_category_exits_2()
    {
        var exit = await Run("cleanup", "list", "--category", "games");

        Assert.Equal(ExitCodes.InvalidInput, exit);
    }

    [Fact]
    public async Task Check_exit_1_when_breaks_found()
    {
        _services.Services["W32Time"] = ServiceStartType.Disabled;

        var exit = await Run("cleanup", "check");

        Assert.Equal(ExitCodes.CompletedWithFailures, exit);
        Assert.Contains("Breaks Windows features:", _out.ToString());
        Assert.Contains("W32Time", _out.ToString());
        Assert.Contains("Fix: sc.exe config W32Time start= demand", _out.ToString());
        Assert.Equal(0, _elevation.RelaunchCalls);
    }

    [Fact]
    public async Task Check_exit_0_on_clean_system()
    {
        _elevation.IsElevated = false;

        var exit = await Run("cleanup", "check");

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Contains("No known problems found.", _out.ToString());
        Assert.Equal(0, _elevation.RelaunchCalls);
    }

    [Fact]
    public async Task Check_reports_a_corrupt_journal()
    {
        _fs.Seed(AppPaths.Default.CleanupJournalFile, "{ not json");

        var exit = await Run("cleanup", "check");

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Contains("The cleanup journal is unreadable", _out.ToString());
    }

    [Fact]
    public async Task Check_reports_rules_that_could_not_run()
    {
        _registry = new UnreadableRegistry();

        var exit = await Run("cleanup", "check");

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Matches(@"[1-9]\d* checks could not be run \(see log\)\.", _out.ToString());
    }

    [Fact]
    public async Task Check_reports_rules_that_could_not_run_in_Portuguese()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("pt-BR");
        _registry = new UnreadableRegistry();

        await Run("cleanup", "check");

        Assert.Contains("verificações não puderam ser executadas (veja o log).", _out.ToString());
    }

    [Fact]
    public async Task Check_marks_items_another_user_must_undo()
    {
        SeedOtherUserEntry();

        await Run("cleanup", "check");

        Assert.Contains("  privacy.theirs (undo as the user who applied it)", _out.ToString());
    }

    [Fact]
    public async Task Check_marks_items_another_user_must_undo_in_Portuguese()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("pt-BR");
        SeedOtherUserEntry();

        await Run("cleanup", "check");

        Assert.Contains("  privacy.theirs (reverta com o usuário que o aplicou)", _out.ToString());
    }

    private void SeedOtherUserEntry() =>
        new JournalStore(_fs, AppPaths.Default).Save(new CleanupJournal(1,
            [new JournalEntry("privacy.theirs", DateTimeOffset.Now, [], null, "S-1-5-21-9-9-9-1002")]));

    [Fact]
    public async Task Undo_requires_ids_or_all()
    {
        Assert.Equal(ExitCodes.InvalidInput, await Run("cleanup", "undo"));
        Assert.Equal(ExitCodes.InvalidInput, await Run("cleanup", "undo", "apps.news", "--all"));
        Assert.Equal(0, _elevation.RelaunchCalls);
    }

    [Fact]
    public async Task Undo_unknown_id_exits_2()
    {
        _elevation.IsElevated = false;

        var exit = await Run("cleanup", "undo", "apps.nope");

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("apps.nope", _out.ToString());
        Assert.Equal(0, _elevation.RelaunchCalls);
    }

    [Fact]
    public async Task Undo_accepts_an_id_only_the_journal_knows()
    {
        new JournalStore(_fs, AppPaths.Default).Save(new CleanupJournal(1,
            [new JournalEntry("privacy.retired", DateTimeOffset.Now, [], null)]));

        var exit = await Run("cleanup", "undo", "privacy.retired");

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Contains("privacy.retired: reverted", _out.ToString());
    }

    [Fact]
    public async Task Undo_declined_elevation_exits_5()
    {
        _elevation.IsElevated = false;
        _elevation.RelaunchResult = null;

        var exit = await Run("cleanup", "undo", "--all");

        Assert.Equal(ExitCodes.ElevationDeclined, exit);
        Assert.Equal(1, _elevation.RelaunchCalls);
    }

    [Fact]
    public async Task Undo_reports_reverted_and_skipped_items()
    {
        var item = _catalog.Items.First(i => i.Actions.All(a => a is RegistryAction { Kind: not null, Delete: false }));
        var action = (RegistryAction)item.Actions[0];
        _registry.Set(action.Path, action.Name, RegistryKind.Dword, "99");
        var ctx = TestServices.Context(TestServices.Create(files: _fs, services: _services, registry: _registry));
        var applied = await new ItemApplier(new ActionHandlers(() => new Dictionary<Guid, Guid?>()), new JournalStore(_fs, AppPaths.Default))
            .ApplyAsync(item, ctx, TestContext.Current.CancellationToken);
        Assert.Equal(ItemStatus.Applied, applied.Status);

        var exit = await Run("cleanup", "undo", item.Id, "apps.news");

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Contains($"{item.Id}: reverted", _out.ToString());
        Assert.DoesNotContain("applied", _out.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("apps.news: skipped (", _out.ToString());
    }

    [Fact]
    public async Task Undo_with_corrupt_journal_exits_1()
    {
        _fs.Seed(AppPaths.Default.CleanupJournalFile, "{ not json");

        var exit = await Run("cleanup", "undo", "--all");

        Assert.Equal(ExitCodes.CompletedWithFailures, exit);
        Assert.Contains("is unreadable", _out.ToString());
        Assert.Contains("nothing was reverted", _out.ToString());
    }

    [Fact]
    public async Task Undo_prints_localized_lines_in_Portuguese()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("pt-BR");

        var exit = await Run("cleanup", "undo", "--all");

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Contains("Não há nada para reverter.", _out.ToString());
    }

    [Fact]
    public async Task Dry_run_lists_cleanup_items_with_level()
    {
        var profile = Path.Combine(_tempDir, "cleanup.json");
        File.WriteAllText(profile, """{"schemaVersion":1,"name":"T","modules":{"cleanup":{"enabled":true,"level":"aggressive","confirmAggressive":true}}}""");

        var exit = await Run("run", "--profile", profile, "--dry-run");

        Assert.Equal(ExitCodes.Ok, exit);
        var output = _out.ToString();
        Assert.Contains("cleanup.apps", output);
        var safe = _catalog.Items.First(i => i.Level == CleanupLevel.Safe);
        Assert.Contains($"    {safe.Id}  {safe.LocalizedText().Title} [safe]", output);
        var contested = _catalog.Items.First(i => i.Contested);
        Assert.Contains($"    {contested.Id}  {contested.LocalizedText().Title} [aggressive] [contested]", output);
        Assert.Equal(0, _elevation.RelaunchCalls);
        Assert.Equal(0, _fs.WriteCount);
    }

    private sealed class UnreadableRegistry : FakeRegistry
    {
        public override RegistryValue? Read(string path, string name) =>
            throw new NotSupportedException("REG_BINARY is not supported");
    }

    [Fact]
    public void Default_modules_put_drivers_before_cleanup()
    {
        var modules = App().Modules;

        Assert.Equal(["drivers", "cleanup"], modules.Select(m => m.Name));
    }
}
