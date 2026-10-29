using System.Globalization;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Prepare.Checks.Developer;

namespace FormatKit.Prepare.Tests.Checks.Developer;

public class GitReposCheckTests : IDisposable
{
    private const string Git = @"C:\Program Files\Git\cmd\git.exe";
    private const string Secret = "https://user:secret-token@example.com/repo.git";

    private readonly CultureInfo _saved = CultureInfo.CurrentUICulture;

    public GitReposCheckTests() => CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

    public void Dispose() => CultureInfo.CurrentUICulture = _saved;

    private static readonly FileAttributes Hidden = FileAttributes.Directory | FileAttributes.Hidden;

    private static ProcessResult Ok(string stdOut = "") => new(0, stdOut, "", TimeSpan.Zero, false);

    private static void Clean(FakeProcessRunner runner) => Queue(runner);

    private static void Queue(FakeProcessRunner runner, string status = "", string refs = "main\torigin/main\t\n", string stash = "", string remote = "origin\n")
    {
        runner.Enqueue(Git, Ok(status));
        runner.Enqueue(Git, Ok(refs));
        runner.Enqueue(Git, Ok(stash));
        runner.Enqueue(Git, Ok(remote));
    }

    private static async Task<IReadOnlyList<PrepareItem>> RunAsync(
        InMemoryFileSystem files, FakeProcessRunner runner, bool gitInstalled = true, params VolumeInfo[] volumes)
    {
        var tools = new FakeToolLocator();
        if (gitInstalled)
            tools.Add("git.exe", Git);
        var context = PrepareTestContext.Create(TestServices.Create(processes: runner, files: files, tools: tools), volumes);
        return await new GitReposCheck().RunAsync(context, CancellationToken.None);
    }

    private static void Repo(InMemoryFileSystem files, string path) => files.SeedDirectory(path + @"\.git", Hidden);

    [Fact]
    public void Identity()
    {
        var check = new GitReposCheck();
        Assert.Equal("dev.git-repos", check.Id);
        Assert.Equal(PrepareGroup.Developer, check.Group);
        Assert.Equal(5, GitReposCheck.MaxDepth);
    }

    [Fact]
    public async Task One_unsafe_repository_is_a_blocker_and_the_clean_ones_are_counted()
    {
        var files = new InMemoryFileSystem();
        Repo(files, @"C:\Users\joao\src\a");
        Repo(files, @"C:\Users\joao\src\b");
        Repo(files, @"C:\Users\joao\src\c");
        var runner = new FakeProcessRunner();
        Queue(runner, status: "1 .M N... 100644 100644 100644 a b file.txt\n", refs: "main\torigin/main\tahead 2\n");
        Clean(runner);
        Clean(runner);

        var items = await RunAsync(files, runner);

        var blocker = Assert.Single(items, i => i.Status == PrepareStatus.Blocker);
        Assert.Contains(@"C:\Users\joao\src\a", blocker.Title);
        Assert.Contains("1 uncommitted change", blocker.Detail);
        Assert.Contains("main", blocker.Detail);
        Assert.Contains("2 commits", blocker.Detail);
        Assert.Contains("ommit and push", blocker.Action);
        var ok = Assert.Single(items, i => i.Status == PrepareStatus.Ok);
        Assert.Equal("2 repositories, all pushed", ok.Title);
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal("dev.git-repos", i.CheckId));
    }

    [Fact]
    public async Task A_single_clean_repository_uses_the_singular()
    {
        var files = new InMemoryFileSystem();
        Repo(files, @"C:\Users\joao\src\a");
        var runner = new FakeProcessRunner();
        Clean(runner);

        var item = Assert.Single(await RunAsync(files, runner));

        Assert.Equal(PrepareStatus.Ok, item.Status);
        Assert.Equal("1 repository, all pushed", item.Title);
    }

    [Fact]
    public async Task Detail_names_every_kind_of_missing_work()
    {
        var files = new InMemoryFileSystem();
        Repo(files, @"C:\Users\joao\src\a");
        var runner = new FakeProcessRunner();
        Queue(runner,
            status: "? a.txt\n? b.txt\n",
            refs: "main\torigin/main\tahead 1\nfeat\t\t\nold\t\t\n",
            stash: "stash@{0}: x\n",
            remote: "");

        var item = Assert.Single(await RunAsync(files, runner));

        Assert.Equal(PrepareStatus.Blocker, item.Status);
        Assert.Contains("2 uncommitted changes", item.Detail);
        Assert.Contains("main has 1 commit not pushed", item.Detail);
        Assert.Contains("feat, old", item.Detail);
        Assert.Contains("1 stash", item.Detail);
        Assert.Contains("no remote", item.Detail);
    }

    [Fact]
    public async Task A_repository_that_is_not_lost_is_not_inspected()
    {
        var files = new InMemoryFileSystem();
        Repo(files, @"E:\code\app");
        var runner = new FakeProcessRunner();

        var items = await RunAsync(files, runner);

        Assert.Empty(items);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task A_repository_inside_a_lost_vhdx_volume_is_inspected()
    {
        var files = new InMemoryFileSystem();
        Repo(files, @"D:\code\app");
        var runner = new FakeProcessRunner();
        Queue(runner, status: "? a.txt\n");
        var devDrive = new VolumeInfo('D', 1, BitLockerState.Off, @"C:\VHDs\dev.vhdx", null);

        var items = await RunAsync(files, runner, true, devDrive);

        var blocker = Assert.Single(items);
        Assert.Equal(PrepareStatus.Blocker, blocker.Status);
        Assert.Contains(@"D:\code\app", blocker.Title);
        Assert.Contains(@"D:\code\app", runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task A_vhdx_volume_that_is_not_on_the_system_drive_is_not_scanned()
    {
        var files = new InMemoryFileSystem();
        Repo(files, @"D:\code\app");
        var runner = new FakeProcessRunner();
        var devDrive = new VolumeInfo('D', 1, BitLockerState.Off, @"E:\VHDs\dev.vhdx", null);

        Assert.Empty(await RunAsync(files, runner, true, devDrive));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task No_repositories_means_no_items_and_no_git_lookup_needed()
    {
        var items = await RunAsync(new InMemoryFileSystem(), new FakeProcessRunner(), gitInstalled: false);

        Assert.Empty(items);
    }

    [Fact]
    public async Task Missing_git_lists_the_repositories_in_one_not_checked_item()
    {
        var files = new InMemoryFileSystem();
        Repo(files, @"C:\Users\joao\src\a");
        Repo(files, @"C:\Users\joao\src\b");
        var runner = new FakeProcessRunner();

        var item = Assert.Single(await RunAsync(files, runner, gitInstalled: false));

        Assert.Equal(PrepareStatus.NotChecked, item.Status);
        Assert.Contains(@"C:\Users\joao\src\a", item.Detail);
        Assert.Contains(@"C:\Users\joao\src\b", item.Detail);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Dubious_ownership_is_not_checked_with_the_safe_directory_hint()
    {
        var files = new InMemoryFileSystem();
        Repo(files, @"C:\Users\joao\src\a");
        var runner = new FakeProcessRunner();
        runner.Enqueue(Git, new ProcessResult(128, "", "fatal: detected dubious ownership in repository", TimeSpan.Zero, false));

        var item = Assert.Single(await RunAsync(files, runner));

        Assert.Equal(PrepareStatus.NotChecked, item.Status);
        Assert.Contains("git config --global --add safe.directory", item.Action);
        Assert.Contains("C:/Users/joao/src/a", item.Action);
    }

    [Fact]
    public async Task A_failing_repository_is_not_checked_and_the_others_still_run()
    {
        var files = new InMemoryFileSystem();
        Repo(files, @"C:\Users\joao\src\a");
        Repo(files, @"C:\Users\joao\src\b");
        var runner = new FakeProcessRunner();
        runner.Enqueue(Git, new ProcessResult(-1, "", "", TimeSpan.Zero, true));
        Clean(runner);

        var items = await RunAsync(files, runner);

        var failed = Assert.Single(items, i => i.Status == PrepareStatus.NotChecked);
        Assert.Contains(@"C:\Users\joao\src\a", failed.Title);
        Assert.Equal("1 repository, all pushed", Assert.Single(items, i => i.Status == PrepareStatus.Ok).Title);
    }

    [Fact]
    public async Task A_git_executable_that_vanishes_is_not_checked()
    {
        var files = new InMemoryFileSystem();
        Repo(files, @"C:\Users\joao\src\a");
        var runner = new FakeProcessRunner();
        runner.ThrowNotFound(Git);

        var item = Assert.Single(await RunAsync(files, runner));

        Assert.Equal(PrepareStatus.NotChecked, item.Status);
    }

    [Fact]
    public async Task A_remote_url_in_any_output_never_reaches_the_items()
    {
        var files = new InMemoryFileSystem();
        Repo(files, @"C:\Users\joao\src\a");
        Repo(files, @"C:\Users\joao\src\b");
        var runner = new FakeProcessRunner();
        Queue(runner, status: $"# branch.upstream {Secret}\n? a.txt\n", refs: $"main\t{Secret}\tahead 1\n", stash: $"stash@{{0}}: {Secret}\n", remote: $"origin {Secret}\n");
        Queue(runner, status: $"# {Secret}\n", refs: $"main\t{Secret}\t\n", stash: "", remote: $"origin\t{Secret}\n");

        var items = await RunAsync(files, runner);

        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.DoesNotContain("secret-token", i.Title + i.Detail + i.Action));
    }
}
