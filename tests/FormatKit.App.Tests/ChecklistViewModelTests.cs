using FormatKit.App.Localization;
using FormatKit.App.Tests.Fakes;
using FormatKit.App.ViewModels;
using FormatKit.Core;
using FormatKit.Core.Running;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Hosting;
using FormatKit.Prepare;
using FormatKit.Prepare.Rendering;

namespace FormatKit.App.Tests;

public sealed class ChecklistViewModelTests : IDisposable
{
    private sealed class ScriptedCheck(params PrepareItem[] items) : IPrepareCheck
    {
        public string Id => "fake.check";
        public PrepareGroup Group => PrepareGroup.Data;
        public bool Ran { get; private set; }
        public Task? Gate { get; init; }

        public async Task<IReadOnlyList<PrepareItem>> RunAsync(PrepareContext context, CancellationToken ct)
        {
            Ran = true;
            if (Gate is not null)
                await Gate;
            return items;
        }
    }

    private readonly string _dir = Directory.CreateTempSubdirectory("fk-checklist-").FullName;
    private readonly InMemoryFileSystem _files = new();
    private readonly FakeDialogService _dialogs = new();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static PrepareItem Item(PrepareStatus status, PrepareGroup group, string title,
        string? detail = null, string? action = null) =>
        new("test." + title, group, status, title, detail, action);

    private ChecklistViewModel Create(ScriptedCheck check)
    {
        var env = new HostEnvironment(
            TestServices.Create(files: _files, volumes: new FakeVolumes(new VolumeInfo('C', 0, BitLockerState.Off, null, null))),
            new Elevation(),
            new FakeScheduledTasks(),
            new FakeRebooter(),
            new FakeReportWriter(),
            new RunStateStore(_files, new AppPaths(@"C:\FormatKitTest")),
            new Serilog.LoggerConfiguration().CreateLogger(),
            TimeSpan.Zero,
            TimeSpan.Zero);
        var runner = new PrepareRunner(
            env,
            [],
            () => [check],
            () => new PrepareEnvironment(@"C:\Users\joao", @"C:\Users\joao\AppData\Local", @"C:\Users\joao\AppData\Roaming", 'C', "PC-TEST", s => s));
        return new ChecklistViewModel(runner, new ProfileService([new FakeModule("drivers")]), _dialogs);
    }

    private string WriteProfile(string json)
    {
        var path = Path.Combine(_dir, "profile.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public async Task Check_OrdersStatusesBlockerAttentionNotCheckedOk()
    {
        var vm = Create(new ScriptedCheck(
            Item(PrepareStatus.Ok, PrepareGroup.Apps, "ok"),
            Item(PrepareStatus.NotChecked, PrepareGroup.Data, "nc"),
            Item(PrepareStatus.Attention, PrepareGroup.Developer, "att", "found it", "do this"),
            Item(PrepareStatus.Blocker, PrepareGroup.Security, "blk"),
            Item(PrepareStatus.Blocker, PrepareGroup.Apps, "blk2")));

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.Equal(
            [PrepareStatus.Blocker, PrepareStatus.Attention, PrepareStatus.NotChecked, PrepareStatus.Ok],
            vm.Groups.Select(g => g.Status));
        Assert.Equal([false, false, false, true], vm.Groups.Select(g => g.StartsCollapsed));
        Assert.Equal(ConsoleRenderer.StatusTitle(PrepareStatus.Blocker), vm.Groups[0].Title);
        Assert.Equal([PrepareGroup.Security, PrepareGroup.Apps], vm.Groups[0].Sections.Select(s => s.Group));
        Assert.Equal(ConsoleRenderer.GroupTitle(PrepareGroup.Security), vm.Groups[0].Sections[0].Title);
        var row = Assert.Single(vm.Groups[1].Sections[0].Items);
        Assert.Equal(("att", "found it", "do this"), (row.Title, row.Detail, row.Action));
        Assert.Null(vm.Error);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Summary_MatchesCliTexts()
    {
        var blocked = Create(new ScriptedCheck(
            Item(PrepareStatus.Blocker, PrepareGroup.Data, "b"), Item(PrepareStatus.Attention, PrepareGroup.Data, "a")));
        var review = Create(new ScriptedCheck(
            Item(PrepareStatus.Attention, PrepareGroup.Data, "a1"), Item(PrepareStatus.Attention, PrepareGroup.Data, "a2")));
        var safe = Create(new ScriptedCheck(Item(PrepareStatus.Ok, PrepareGroup.Data, "o")));

        await blocked.CheckCommand.ExecuteAsync(null);
        await review.CheckCommand.ExecuteAsync(null);
        await safe.CheckCommand.ExecuteAsync(null);

        Assert.Equal(ConsoleRenderer.Summary(new PrepareReport([Item(PrepareStatus.Blocker, PrepareGroup.Data, "b")])), blocked.Summary);
        Assert.Equal(
            ConsoleRenderer.Summary(new PrepareReport(
                [Item(PrepareStatus.Attention, PrepareGroup.Data, "a1"), Item(PrepareStatus.Attention, PrepareGroup.Data, "a2")])),
            review.Summary);
        Assert.Equal(ConsoleRenderer.Summary(new PrepareReport([])), safe.Summary);
        Assert.Equal(3, new[] { blocked.Summary, review.Summary, safe.Summary }.Distinct().Count());
    }

    [Fact]
    public async Task InvalidProfile_ShowsErrorsAndNoResults()
    {
        var vm = Create(new ScriptedCheck(Item(PrepareStatus.Blocker, PrepareGroup.Data, "b")));
        await vm.CheckCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.Groups);

        vm.ProfilePath = WriteProfile("""{"schemaVersion":1,"name":"Mine","modules":{"nope":{"enabled":true}}}""");
        await vm.CheckCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.ProfileErrors);
        Assert.Empty(vm.Groups);
        Assert.Equal(string.Empty, vm.Summary);
        Assert.False(vm.SaveMarkdownCommand.CanExecute(null));
    }

    [Fact]
    public async Task Check_WithValidProfile_ClearsErrorsAndRuns()
    {
        var check = new ScriptedCheck(Item(PrepareStatus.Ok, PrepareGroup.Data, "o"));
        var vm = Create(check);
        vm.ProfilePath = WriteProfile("""{"schemaVersion":1,"name":"Mine","modules":{"drivers":{"enabled":true}}}""");

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.Empty(vm.ProfileErrors);
        Assert.True(check.Ran);
        Assert.Single(vm.Groups);
    }

    [Fact]
    public async Task Save_OnWindowsDrive_ShowsWarningAndWrites()
    {
        var vm = Create(new ScriptedCheck(Item(PrepareStatus.Ok, PrepareGroup.Data, "o")));
        await vm.CheckCommand.ExecuteAsync(null);
        _files.CreateDirectory(@"C:\out");
        _dialogs.SaveFileResult = @"C:\out\check.md";

        vm.SaveMarkdownCommand.Execute(null);

        Assert.True(_files.FileExists(@"C:\out\check.md"));
        Assert.Contains(Strings.Format("Prepare_OutputWritten", @"C:\out\check.md"), vm.SaveMessage);
        Assert.Contains(Strings.Format("Prepare_OutputOnWindows", @"C:\out\check.md"), vm.SaveMessage);
    }

    [Fact]
    public async Task Save_WriteFails_ShowsFailureOnly()
    {
        var vm = Create(new ScriptedCheck(Item(PrepareStatus.Ok, PrepareGroup.Data, "o")));
        await vm.CheckCommand.ExecuteAsync(null);
        _dialogs.SaveFileResult = @"C:\missing\check.md";

        vm.SaveMarkdownCommand.Execute(null);

        Assert.StartsWith(Strings.Get("Prepare_OutputFailed").Split('{')[0], vm.SaveMessage);
        Assert.DoesNotContain(Strings.Get("Prepare_OutputWritten").Split('{')[0], vm.SaveMessage);
    }

    [Fact]
    public async Task Save_Cancelled_DoesNothing()
    {
        var vm = Create(new ScriptedCheck(Item(PrepareStatus.Ok, PrepareGroup.Data, "o")));
        await vm.CheckCommand.ExecuteAsync(null);
        _dialogs.SaveFileResult = null;

        vm.SaveMarkdownCommand.Execute(null);

        Assert.Null(vm.SaveMessage);
        Assert.Contains(_dialogs.Calls, c => c.StartsWith("SaveFile:"));
    }

    [Fact]
    public void Save_BeforeAnyCheck_IsDisabled()
    {
        var vm = Create(new ScriptedCheck());

        Assert.False(vm.SaveMarkdownCommand.CanExecute(null));
    }

    [Fact]
    public void BrowseProfile_SetsPathAndCancelKeepsIt()
    {
        var vm = Create(new ScriptedCheck());
        _dialogs.OpenFileResult = @"C:\p\profile.json";
        vm.BrowseProfileCommand.Execute(null);
        Assert.Equal(@"C:\p\profile.json", vm.ProfilePath);

        _dialogs.OpenFileResult = null;
        vm.BrowseProfileCommand.Execute(null);
        Assert.Equal(@"C:\p\profile.json", vm.ProfilePath);

        vm.ClearProfileCommand.Execute(null);
        Assert.Null(vm.ProfilePath);
    }

    [Fact]
    public void StatusStyle_GivesEveryStatusADistinctGlyphAndColor()
    {
        var statuses = Enum.GetValues<PrepareStatus>();

        Assert.Equal(statuses.Length, statuses.Select(StatusStyle.Glyph).Distinct().Count());
        Assert.Equal(statuses.Length, statuses.Select(StatusStyle.Color).Distinct().Count());
        Assert.All(statuses, s => Assert.False(string.IsNullOrEmpty(StatusStyle.Glyph(s))));
    }

    [Fact]
    public async Task Check_RowsAndGroupsCarryTheirStatusStyle()
    {
        var vm = Create(new ScriptedCheck(Item(PrepareStatus.Blocker, PrepareGroup.Data, "b")));

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.Equal(StatusStyle.Glyph(PrepareStatus.Blocker), vm.Groups[0].Glyph);
        Assert.Equal(StatusStyle.Color(PrepareStatus.Blocker), vm.Groups[0].Color);
        Assert.Equal(StatusStyle.Color(PrepareStatus.Blocker), vm.Groups[0].Sections[0].Items[0].Color);
    }

    [Fact]
    public async Task Check_IsDisabledWhileRunning()
    {
        var gate = new TaskCompletionSource();
        var vm = Create(new ScriptedCheck { Gate = gate.Task });

        var running = vm.CheckCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        Assert.False(vm.CheckCommand.CanExecute(null));

        gate.SetResult();
        await running;
        Assert.True(vm.CheckCommand.CanExecute(null));
    }

    [Fact]
    public async Task Save_Failure_IsFlaggedAsErrorAndWarningIsNot()
    {
        var vm = Create(new ScriptedCheck(Item(PrepareStatus.Ok, PrepareGroup.Data, "o")));
        await vm.CheckCommand.ExecuteAsync(null);

        _dialogs.SaveFileResult = @"C:\missing\check.md";
        vm.SaveMarkdownCommand.Execute(null);
        Assert.True(vm.SaveFailed);

        _files.CreateDirectory(@"C:\out");
        _dialogs.SaveFileResult = @"C:\out\check.md";
        vm.SaveMarkdownCommand.Execute(null);
        Assert.False(vm.SaveFailed);
    }
}
