using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Dev.Localization;
using FormatKit.Dev.Steps;
using static FormatKit.Dev.Tests.DevTestData;

namespace FormatKit.Dev.Tests.Steps;

public class DotfilesStepTests
{
    private const string GitExe = @"C:\Program Files\Git\cmd\git.exe";
    private const string Repo = "https://github.com/joao/dotfiles.git";
    private const string Clone = @"C:\Users\Joao\dotfiles";
    private const string LocalFolder = @"D:\dotfiles";
    private const string Target = @"C:\Users\Joao\.gitconfig.local";
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 5, 7, TimeSpan.Zero);

    private sealed class Rig
    {
        public FakeProcessRunner Processes { get; } = new();
        public FakeInteractiveConsole Console { get; } = new();
        public FakeToolLocator Tools { get; } = new();
        public InMemoryFileSystem Files { get; } = new();

        public Rig(bool git = true, bool canPrompt = true)
        {
            if (git)
                Tools.Add("git.exe", GitExe);
            Console.CanPrompt = canPrompt;
        }

        public async Task<(IReadOnlyList<string> Messages, bool Success)> Run(DotfilesOptions options)
        {
            var result = await Execute(options);
            return (result.Messages, result.Outcome == StepOutcome.Succeeded);
        }

        public Task<StepResult> Execute(DotfilesOptions options)
        {
            var ctx = TestServices.Context(TestServices.Create(processes: Processes, files: Files, console: Console, tools: Tools));
            return new DotfilesStep(options, Env(), new FixedTime(Now)).ExecuteAsync(ctx, TestContext.Current.CancellationToken);
        }

        public void Origin(string url, int exit = 0) => Processes.Enqueue(GitExe, new ProcessResult(exit, url + "\n", "", TimeSpan.Zero, false));

        public void Git(int exit) => Processes.Enqueue(GitExe, new ProcessResult(exit, "", "", TimeSpan.Zero, false));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static DotfilesOptions FromRepo(params DotfileEntry[] files) =>
        new(Repo, null, Clone, files.Length == 0 ? [new DotfileEntry("git/.gitconfig.local", Target)] : files);

    private static DotfilesOptions FromFolder(params DotfileEntry[] files) =>
        new(null, LocalFolder, Clone, files.Length == 0 ? [new DotfileEntry("git/.gitconfig.local", Target)] : files);

    private static string Failed => Strings.Get("Outcome_Failed");

    [Fact]
    public async Task Clones_when_missing()
    {
        var rig = new Rig();
        rig.Console.ExitCode = _ =>
        {
            rig.Files.Seed(Clone + @"\git\.gitconfig.local", "x"); // what the clone leaves behind
            return 0;
        };

        var (messages, success) = await rig.Run(FromRepo());

        Assert.True(success);
        var call = Assert.Single(rig.Console.Attached);
        Assert.Equal(GitExe, call.FileName);
        Assert.Equal(["clone", "--", Repo, Clone], call.Arguments);
        Assert.Equal($"repository: {Strings.Get("Outcome_Done")}", messages[0]);
        Assert.Equal($"{Target}: {Strings.Get("Outcome_Done")}", messages[1]);
    }

    [Fact]
    public async Task Pulls_when_same_origin()
    {
        var rig = new Rig();
        rig.Files.CreateDirectory(Clone);
        rig.Files.Seed(Clone + @"\git\.gitconfig.local", "x");
        rig.Files.Seed(Target, "x");
        rig.Origin(Repo);
        rig.Git(0);

        var (messages, success) = await rig.Run(FromRepo());

        Assert.True(success);
        Assert.Equal(["-C", Clone, "remote", "get-url", "origin"], rig.Processes.Calls[0].Arguments);
        Assert.Equal(["-C", Clone, "pull", "--ff-only"], Assert.Single(rig.Console.Attached).Arguments);
        Assert.Equal($"repository: {Strings.Get("Outcome_Done")}", messages[0]);
    }

    [Fact]
    public async Task Other_origin_fails_and_skips_files()
    {
        var rig = new Rig();
        rig.Files.CreateDirectory(Clone);
        rig.Files.Seed(Clone + @"\git\.gitconfig.local", "x");
        rig.Origin("https://github.com/other/thing.git");

        var result = await rig.Execute(FromRepo());

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(Strings.Get("Next_OtherCloneTo"), result.NextAction);
        Assert.Empty(rig.Console.Attached);
        Assert.False(rig.Files.FileExists(Target));
        Assert.Equal(0, rig.Files.WriteCount);
    }

    [Fact]
    public async Task Not_a_repository_fails_with_other_clone_to()
    {
        var rig = new Rig();
        rig.Files.CreateDirectory(Clone);
        rig.Origin("", exit: 128);

        var result = await rig.Execute(FromRepo());

        Assert.Equal(Strings.Get("Next_OtherCloneTo"), result.NextAction);
        Assert.Empty(rig.Console.Attached);
    }

    [Fact]
    public async Task Missing_git_fails_with_add_git()
    {
        var rig = new Rig(git: false);

        var result = await rig.Execute(FromRepo());

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(Strings.Get("Next_AddGit"), result.NextAction);
    }

    [Fact]
    public async Task Unattended_clone_disables_terminal_prompt()
    {
        var rig = new Rig(canPrompt: false);
        rig.Git(0);
        rig.Processes.OnRun = _ => rig.Files.Seed(Clone + @"\git\.gitconfig.local", "x");

        var (_, success) = await rig.Run(FromRepo());

        Assert.True(success);
        Assert.Empty(rig.Console.Attached);
        var call = Assert.Single(rig.Processes.Calls);
        Assert.Equal(["clone", "--", Repo, Clone], call.Arguments);
        Assert.Equal("0", call.Environment!["GIT_TERMINAL_PROMPT"]);
    }

    [Fact]
    public async Task Attended_clone_runs_attached()
    {
        var rig = new Rig();
        rig.Processes.OnRun = _ => throw new InvalidOperationException("must not be captured");
        rig.Console.ExitCode = _ =>
        {
            rig.Files.Seed(Clone + @"\git\.gitconfig.local", "x");
            return 0;
        };

        var (_, success) = await rig.Run(FromRepo());

        Assert.True(success);
        Assert.Equal(["clone", "--", Repo, Clone], Assert.Single(rig.Console.Attached).Arguments);
        Assert.Empty(rig.Processes.Calls);
    }

    [Fact]
    public async Task Failed_clone_fails_with_see_log_and_skips_files()
    {
        var rig = new Rig { Console = { ExitCode = _ => 128 } };

        var result = await rig.Execute(FromRepo());

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(Strings.Get("Next_SeeLog"), result.NextAction);
        Assert.Equal(0, rig.Files.WriteCount);
    }

    [Fact]
    public async Task Identical_file_already_in_order()
    {
        var rig = new Rig();
        rig.Files.Seed(LocalFolder + @"\git\.gitconfig.local", "same");
        rig.Files.Seed(Target, "same");

        var (messages, success) = await rig.Run(FromFolder());

        Assert.True(success);
        Assert.Equal($"{Target}: {Strings.Get("Outcome_AlreadyInOrder")}", messages[1]);
        Assert.Equal(0, rig.Files.WriteCount);
    }

    [Fact]
    public async Task Different_file_backed_up_then_copied()
    {
        var rig = new Rig();
        rig.Files.Seed(LocalFolder + @"\git\.gitconfig.local", "new");
        rig.Files.Seed(Target, "old");

        var (_, success) = await rig.Run(FromFolder());

        Assert.True(success);
        Assert.Equal("new", rig.Files.ReadAllText(Target));
        Assert.Equal("old", rig.Files.ReadAllText(Backup.PathFor(Target, Now)));
    }

    [Fact]
    public async Task Crlf_only_difference_is_backed_up_and_replaced()
    {
        var rig = new Rig();
        rig.Files.Seed(LocalFolder + @"\git\.gitconfig.local", "a\nb\n");
        rig.Files.Seed(Target, "a\r\nb\r\n");

        var (_, success) = await rig.Run(FromFolder());

        Assert.True(success);
        Assert.Equal("a\nb\n", rig.Files.ReadAllText(Target));
        Assert.Equal("a\r\nb\r\n", rig.Files.ReadAllText(Backup.PathFor(Target, Now)));
    }

    [Fact]
    public async Task Missing_target_creates_folder_and_copies()
    {
        var rig = new Rig();
        var deep = @"C:\Users\Joao\Documents\PowerShell\profile.ps1";
        rig.Files.Seed(LocalFolder + @"\ps\profile.ps1", "p");

        var (_, success) = await rig.Run(FromFolder(new DotfileEntry("ps/profile.ps1", deep)));

        Assert.True(success);
        Assert.True(rig.Files.DirectoryExists(@"C:\Users\Joao\Documents\PowerShell"));
        Assert.Equal("p", rig.Files.ReadAllText(deep));
    }

    [Fact]
    public async Task Missing_source_fails_only_that_file()
    {
        var rig = new Rig();
        rig.Files.Seed(LocalFolder + @"\ok.txt", "ok");
        var okTarget = @"C:\Users\Joao\ok.txt";

        var (messages, success) = await rig.Run(FromFolder(new DotfileEntry("gone.txt", Target), new DotfileEntry("ok.txt", okTarget)));

        Assert.False(success);
        Assert.StartsWith($"{Target}: {Failed}", messages[1]);
        Assert.Equal($"{okTarget}: {Strings.Get("Outcome_Done")}", messages[2]);
        Assert.Equal("ok", rig.Files.ReadAllText(okTarget));
        Assert.False(rig.Files.FileExists(Target));
    }

    [Fact]
    public async Task Local_path_uses_folder_in_place()
    {
        var rig = new Rig();
        rig.Files.Seed(LocalFolder + @"\git\.gitconfig.local", "x");

        var (messages, success) = await rig.Run(FromFolder());

        Assert.True(success);
        Assert.Equal($"repository: {Strings.Get("Outcome_NotApplicable")}", messages[0]);
        Assert.Empty(rig.Processes.Calls);
        Assert.Empty(rig.Console.Attached);
        Assert.Equal("x", rig.Files.ReadAllText(Target));
    }

    [Theory]
    [InlineData(@"sub\..\..\secret.txt")] // normalizes out of the root
    [InlineData(@".. \secret.txt")]
    [InlineData(@"C:\Windows\secret.txt")] // absolute
    [InlineData(@"..\dotfiles-evil\secret.txt")] // sibling sharing the root's name as a prefix
    public async Task From_escaping_the_root_fails_that_file(string from)
    {
        var rig = new Rig();
        rig.Files.Seed(@"D:\secret.txt", "s");
        rig.Files.Seed(@"D:\dotfiles-evil\secret.txt", "s");
        rig.Files.Seed(@"C:\Windows\secret.txt", "s");

        var (messages, success) = await rig.Run(FromFolder(new DotfileEntry(from, Target)));

        Assert.False(success);
        Assert.StartsWith($"{Target}: {Failed}", messages[1]);
        Assert.False(rig.Files.FileExists(Target));
        Assert.Equal(0, rig.Files.WriteCount);
    }

    [Theory]
    [InlineData(@"C:\Users\Joao\..\Other\x")]
    [InlineData(@"C:\Users\JoaoOther\x")]
    [InlineData(@"C:\Users\Joao")]
    public async Task To_outside_the_profile_fails_that_file(string to)
    {
        var rig = new Rig();
        rig.Files.Seed(LocalFolder + @"\a.txt", "a");

        var (messages, success) = await rig.Run(FromFolder(new DotfileEntry("a.txt", to)));

        Assert.False(success);
        Assert.StartsWith($"{to}: {Failed}", messages[1]);
        Assert.Equal(0, rig.Files.WriteCount);
    }
}
