using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Prepare.Git;

namespace FormatKit.Prepare.Tests.Git;

public class GitRepoInspectorTests
{
    private const string Git = @"C:\Program Files\Git\cmd\git.exe";
    private const string Repo = @"C:\Users\joao\src\a";

    private static ProcessResult Ok(string stdOut = "") => new(0, stdOut, "", TimeSpan.Zero, false);

    private static void Enqueue(FakeProcessRunner runner, string status = "", string refs = "", string stash = "", string remote = "origin\n")
    {
        runner.Enqueue(Git, Ok(status));
        runner.Enqueue(Git, Ok(refs));
        runner.Enqueue(Git, Ok(stash));
        runner.Enqueue(Git, Ok(remote));
    }

    [Fact]
    public void Find_returns_repositories_with_a_git_folder_or_file_and_skips_the_rest()
    {
        var files = new InMemoryFileSystem();
        files.SeedDirectory(@"C:\Users\joao\src\a\.git", FileAttributes.Directory | FileAttributes.Hidden);
        files.SeedDirectory(@"C:\Users\joao\src\a\inner\.git", FileAttributes.Directory | FileAttributes.Hidden);
        files.Seed(@"C:\Users\joao\src\b\.git", "gitdir: ../a/.git/worktrees/b");
        files.SeedDirectory(@"C:\Users\joao\AppData\x\.git", FileAttributes.Directory | FileAttributes.Hidden);
        files.SeedDirectory(@"C:\Users\joao\web\node_modules\y\.git", FileAttributes.Directory | FileAttributes.Hidden);
        files.SeedDirectory(@"C:\Users\joao\1\2\3\4\5\6\.git", FileAttributes.Directory | FileAttributes.Hidden);
        files.SeedDirectory(@"C:\Users\joao\1\2\3\4\5\deep\.git", FileAttributes.Directory | FileAttributes.Hidden);
        var inspector = new GitRepoInspector(new FakeProcessRunner(), files, Git);

        var found = inspector.Find(@"C:\Users\joao", 5, _ => { });

        Assert.Equal([@"C:\Users\joao\src\a", @"C:\Users\joao\src\b"], found.OrderBy(p => p).ToArray());
    }

    [Fact]
    public void Find_reports_unreadable_folders_and_keeps_going()
    {
        var files = new InMemoryFileSystem();
        files.SeedDirectory(@"C:\Users\joao\locked");
        files.MarkUnreadable(@"C:\Users\joao\locked");
        files.SeedDirectory(@"C:\Users\joao\ok\.git", FileAttributes.Directory | FileAttributes.Hidden);
        var unreadable = new List<string>();

        var found = new GitRepoInspector(new FakeProcessRunner(), files, Git).Find(@"C:\Users\joao", 5, unreadable.Add);

        Assert.Equal([@"C:\Users\joao\ok"], found);
        Assert.Equal([@"C:\Users\joao\locked"], unreadable);
    }

    [Fact]
    public void Find_scans_a_volume_root()
    {
        var files = new InMemoryFileSystem();
        files.SeedDirectory(@"D:\code\app\.git", FileAttributes.Directory | FileAttributes.Hidden);

        var found = new GitRepoInspector(new FakeProcessRunner(), files, Git).Find(@"D:\", 5, _ => { });

        Assert.Equal([@"D:\code\app"], found);
    }

    [Fact]
    public async Task InspectAsync_runs_four_read_only_queries_with_the_git_environment()
    {
        var runner = new FakeProcessRunner();
        Enqueue(runner);

        await new GitRepoInspector(runner, new InMemoryFileSystem(), Git).InspectAsync(Repo, CancellationToken.None);

        Assert.Equal(4, runner.Calls.Count);
        Assert.All(runner.Calls, c =>
        {
            Assert.Equal(Git, c.FileName);
            Assert.Equal(["-C", Repo], c.Arguments.Take(2));
            Assert.Equal(FormatKit.Core.Timeouts.Query, c.Timeout);
            Assert.Equal("C", c.Environment!["LC_ALL"]);
            Assert.Equal("0", c.Environment["GIT_TERMINAL_PROMPT"]);
            Assert.Equal("0", c.Environment["GIT_OPTIONAL_LOCKS"]);
            Assert.DoesNotContain("-v", c.Arguments);
            Assert.DoesNotContain(c.Arguments, a => a.Contains("get-url") || a.Contains("remote."));
        });
        Assert.Equal(["status", "--porcelain=v2", "--branch"], runner.Calls[0].Arguments.Skip(2));
        Assert.Equal(
            ["for-each-ref", "--format=%(refname:short)%09%(upstream:short)%09%(upstream:track,nobracket)", "refs/heads"],
            runner.Calls[1].Arguments.Skip(2));
        Assert.Equal(["stash", "list"], runner.Calls[2].Arguments.Skip(2));
        Assert.Equal(["remote"], runner.Calls[3].Arguments.Skip(2));
    }

    [Fact]
    public async Task InspectAsync_parses_status_branches_stashes_and_remote()
    {
        var runner = new FakeProcessRunner();
        Enqueue(runner,
            status: "# branch.oid abc\n# branch.head main\n# branch.ab +2 -0\n1 .M N... 100644 100644 100644 a b file.txt\n"
                    + "2 R. N... 100644 100644 100644 a b R100 new.txt\told.txt\nu UU N... 1 2 3 4 a b c conflict.txt\n"
                    + "? new.txt\n! ignored.log\n",
            refs: "main\torigin/main\tahead 2\nfeat\t\t\nold\torigin/old\tgone\ndev\torigin/dev\tbehind 3\n",
            stash: "stash@{0}: WIP on main\nstash@{1}: WIP on feat\n",
            remote: "");

        var state = await new GitRepoInspector(runner, new InMemoryFileSystem(), Git).InspectAsync(Repo, CancellationToken.None);

        Assert.Equal(4, state.Uncommitted);
        Assert.Equal([new BranchAhead("main", 2)], state.Ahead);
        Assert.Equal(["feat", "old"], state.NoUpstream);
        Assert.Equal(2, state.Stashes);
        Assert.False(state.HasRemote);
        Assert.False(state.IsSafe);
    }

    [Fact]
    public async Task InspectAsync_reads_ahead_when_the_branch_is_also_behind()
    {
        var runner = new FakeProcessRunner();
        Enqueue(runner, refs: "main\torigin/main\tahead 1, behind 4\n");

        var state = await new GitRepoInspector(runner, new InMemoryFileSystem(), Git).InspectAsync(Repo, CancellationToken.None);

        Assert.Equal([new BranchAhead("main", 1)], state.Ahead);
    }

    [Fact]
    public async Task A_clean_pushed_repository_is_safe()
    {
        var runner = new FakeProcessRunner();
        Enqueue(runner, status: "# branch.head main\n", refs: "main\torigin/main\t\n");

        var state = await new GitRepoInspector(runner, new InMemoryFileSystem(), Git).InspectAsync(Repo, CancellationToken.None);

        Assert.True(state.IsSafe);
        Assert.True(state.HasRemote);
    }

    [Fact]
    public async Task Dubious_ownership_is_reported_as_such()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(Git, new ProcessResult(128, "", "fatal: detected dubious ownership in repository at 'C:/x'", TimeSpan.Zero, false));

        var ex = await Assert.ThrowsAsync<GitInspectionException>(() =>
            new GitRepoInspector(runner, new InMemoryFileSystem(), Git).InspectAsync(Repo, CancellationToken.None));

        Assert.True(ex.IsDubiousOwnership);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(128, false)]
    public async Task Other_failures_are_not_dubious_ownership(int exitCode, bool timedOut)
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(Git, new ProcessResult(exitCode, "", "fatal: not a git repository", TimeSpan.Zero, timedOut));

        var ex = await Assert.ThrowsAsync<GitInspectionException>(() =>
            new GitRepoInspector(runner, new InMemoryFileSystem(), Git).InspectAsync(Repo, CancellationToken.None));

        Assert.False(ex.IsDubiousOwnership);
    }

    [Fact]
    public async Task A_timeout_throws()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(Git, new ProcessResult(-1, "", "", TimeSpan.Zero, true));

        var ex = await Assert.ThrowsAsync<GitInspectionException>(() =>
            new GitRepoInspector(runner, new InMemoryFileSystem(), Git).InspectAsync(Repo, CancellationToken.None));

        Assert.False(ex.IsDubiousOwnership);
    }
}
