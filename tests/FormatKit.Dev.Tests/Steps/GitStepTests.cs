using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Dev.Localization;
using FormatKit.Dev.Steps;

namespace FormatKit.Dev.Tests.Steps;

public class GitStepTests
{
    private const string GitExe = @"C:\Program Files\Git\cmd\git.exe";

    private sealed class Rig
    {
        public FakeProcessRunner Processes { get; } = new();
        public FakeToolLocator Tools { get; } = new();

        public Rig(bool gitPresent = true)
        {
            if (gitPresent)
                Tools.Add("git.exe", GitExe);
        }

        public StepContext Context() => TestServices.Context(TestServices.Create(processes: Processes, tools: Tools));

        public void Git(int exit, string stdout = "") =>
            Processes.Enqueue(GitExe, new ProcessResult(exit, stdout, "", TimeSpan.Zero, false));

        public IEnumerable<string> Commands => Processes.Calls.Select(c => string.Join(' ', c.Arguments));
    }

    [Fact]
    public async Task Git_sets_only_differing_keys()
    {
        var rig = new Rig();
        rig.Git(0, "Joao\n");  // user.name: equal
        rig.Git(0, "old@example.com\n"); // user.email: differs
        rig.Git(0);            // set user.email
        rig.Git(1);            // init.defaultBranch: unset
        rig.Git(0);            // set init.defaultBranch
        var step = new GitStep(new GitOptions("Joao", "joao@example.com", "main", null, null));

        var result = await step.ExecuteAsync(rig.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Equal(
        [
            "config --global --get user.name",
            "config --global --get user.email",
            "config --global user.email joao@example.com",
            "config --global --get init.defaultBranch",
            "config --global init.defaultBranch main",
        ], rig.Commands);
        Assert.Equal("user.name: " + Strings.Get("Outcome_AlreadyInOrder"), result.Messages[0]);
        Assert.Equal("user.email: " + Strings.Get("Outcome_Done"), result.Messages[1]);
        Assert.Equal("init.defaultBranch: " + Strings.Get("Outcome_Done"), result.Messages[2]);
    }

    [Fact]
    public async Task Git_missing_fails_with_add_git()
    {
        var rig = new Rig(gitPresent: false);
        var step = new GitStep(new GitOptions("Joao", "joao@example.com", null, "code --wait", "input"));

        var result = await step.ExecuteAsync(rig.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(Strings.Get("Next_AddGit"), result.NextAction);
        Assert.Equal(4, result.Messages.Count);
        Assert.All(result.Messages, m => Assert.Contains(Strings.Get("Outcome_Failed"), m));
        Assert.Empty(rig.Processes.Calls);
    }

    [Fact]
    public async Task Git_found_through_known_path_when_path_is_stale()
    {
        var rig = new Rig();
        rig.Git(0, "main\n");
        var step = new GitStep(new GitOptions(null, null, "main", null, null));

        var result = await step.ExecuteAsync(rig.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Equal(GitExe, Assert.Single(rig.Processes.Calls).FileName);
    }

    [Fact]
    public async Task Git_config_failure_fails_the_item()
    {
        var rig = new Rig();
        rig.Git(1);
        rig.Git(1);
        var step = new GitStep(new GitOptions("Joao", null, null, null, null));

        var result = await step.ExecuteAsync(rig.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(Strings.Get("Next_SeeLog"), result.NextAction);
    }

    [Fact]
    public void Id_and_admin()
    {
        var step = new GitStep(new GitOptions("Joao", null, null, null, null));

        Assert.Equal("dev.git", step.Id);
        Assert.False(step.RequiresAdmin);
    }
}
