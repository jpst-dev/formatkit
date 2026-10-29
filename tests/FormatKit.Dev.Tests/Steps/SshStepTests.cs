using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Dev.Localization;
using FormatKit.Dev.Steps;
using static FormatKit.Dev.Tests.DevTestData;

namespace FormatKit.Dev.Tests.Steps;

public class SshStepTests
{
    private const string Keygen = @"C:\Windows\System32\OpenSSH\ssh-keygen.exe";
    private const string Add = @"C:\Windows\System32\OpenSSH\ssh-add.exe";
    private const string GitExe = @"C:\Program Files\Git\cmd\git.exe";
    private const string KeyFile = @"C:\Users\Joao\.ssh\id_ed25519";
    private const string PubKey = "ssh-ed25519 AAAAC3Nza joao@new-pc";
    private const string Fingerprint = "SHA256:abc123";

    private sealed class Rig
    {
        public FakeProcessRunner Processes { get; } = new();
        public FakeInteractiveConsole Console { get; } = new();
        public FakeServiceControl Services { get; } = new();
        public FakeToolLocator Tools { get; } = new();
        public InMemoryFileSystem Files { get; } = new();

        public Rig(bool git = true, bool key = true, int sc = 1056)
        {
            Tools.Add("ssh-keygen.exe", Keygen);
            Tools.Add("ssh-add.exe", Add);
            if (git)
                Tools.Add("git.exe", GitExe);
            Services.Services["ssh-agent"] = ServiceStartType.Automatic;
            Processes.Enqueue("sc.exe", Result(sc)); // 1056: already running unless a test says otherwise
            if (key)
                Files.Seed(KeyFile + ".pub", PubKey + "\r\n");
            if (key)
                Files.Seed(KeyFile, "PRIVATE");
        }

        public StepContext Context() =>
            TestServices.Context(TestServices.Create(
                processes: Processes, files: Files, services: Services, console: Console, tools: Tools));

        public void Fingerprints(string loaded, int loadedExit = 0)
        {
            Processes.Enqueue(Keygen, Result(0, $"256 {Fingerprint} joao@new-pc (ED25519)\r\n"));
            Processes.Enqueue(Add, Result(loadedExit, loaded));
        }

        public void GitConfig(int getExit, string value = "") => Processes.Enqueue(GitExe, Result(getExit, value));

        public static ProcessResult Result(int exit, string stdout = "") => new(exit, stdout, "", TimeSpan.Zero, false);
    }

    private static readonly SshOptions Ed25519 = new("ed25519", "joao@new-pc");

    private static Task<IReadOnlyList<DevItem>> Run(Rig rig, SshOptions? options = null) =>
        SshStep.RunAsync(options ?? Ed25519, Env(), rig.Context(), TestContext.Current.CancellationToken);

    private static DevItem Item(IReadOnlyList<DevItem> items, string label) => items.Single(i => i.Label == label);

    [Fact]
    public async Task Ssh_generates_key_attached_with_rsa_bits()
    {
        var rig = new Rig(key: false);
        rig.GitConfig(0, KnownTools.WindowsSsh);

        var items = await Run(rig, new SshOptions("rsa", "joao@new-pc"));

        var attached = Assert.Single(rig.Console.Attached);
        Assert.Equal(Keygen, attached.FileName);
        Assert.Equal(["-t", "rsa", "-b", "4096", "-C", "joao@new-pc", "-f", @"C:\Users\Joao\.ssh\id_rsa"], attached.Arguments);
        Assert.Equal(DevItemOutcome.Done, Item(items, "key").Outcome);
    }

    [Fact]
    public async Task Ssh_ed25519_has_no_bits_argument()
    {
        var rig = new Rig(key: false);
        rig.GitConfig(0, KnownTools.WindowsSsh);

        await Run(rig);

        Assert.Equal(["-t", "ed25519", "-C", "joao@new-pc", "-f", KeyFile], Assert.Single(rig.Console.Attached).Arguments);
    }

    [Fact]
    public async Task Ssh_existing_key_kept()
    {
        var rig = new Rig();
        rig.Fingerprints($"256 {Fingerprint} joao@new-pc (ED25519)");
        rig.GitConfig(0, KnownTools.WindowsSsh);

        var items = await Run(rig);

        Assert.Equal(DevItemOutcome.AlreadyInOrder, Item(items, "key").Outcome);
        Assert.Empty(rig.Console.Attached);
    }

    [Fact]
    public async Task Ssh_nobody_at_pc_pending_and_never_attaches()
    {
        var rig = new Rig(key: false) { Console = { CanPrompt = false } };
        rig.GitConfig(0, KnownTools.WindowsSsh);

        var items = await Run(rig);

        var key = Item(items, "key");
        Assert.Equal(DevItemOutcome.Pending, key.Outcome);
        Assert.Equal(Strings.Get("Next_RunSshKey"), key.NextAction);
        Assert.Equal(DevItemOutcome.NotApplicable, Item(items, "agent key").Outcome);
        Assert.Empty(rig.Console.Attached);
    }

    [Fact]
    public async Task Ssh_nobody_at_pc_leaves_agent_key_pending_and_never_attaches()
    {
        var rig = new Rig { Console = { CanPrompt = false } };
        rig.Fingerprints("The agent has no identities.", loadedExit: 1);
        rig.GitConfig(0, KnownTools.WindowsSsh);

        var items = await Run(rig);

        var agentKey = Item(items, "agent key");
        Assert.Equal(DevItemOutcome.Pending, agentKey.Outcome);
        Assert.Equal(Strings.Get("Next_RunSshKey"), agentKey.NextAction);
        Assert.Empty(rig.Console.Attached);
    }

    [Fact]
    public async Task Ssh_agent_set_automatic_and_started()
    {
        var rig = new Rig(sc: 0);
        rig.Services.Services["ssh-agent"] = ServiceStartType.Disabled;
        rig.Fingerprints($"256 {Fingerprint} x");
        rig.GitConfig(0, KnownTools.WindowsSsh);

        var items = await Run(rig);

        Assert.Equal([("ssh-agent", ServiceStartType.Automatic)], rig.Services.Calls);
        var sc = rig.Processes.Calls.Single(c => c.FileName == "sc.exe");
        Assert.Equal(["start", "ssh-agent"], sc.Arguments);
        Assert.Equal(DevItemOutcome.Done, Item(items, "ssh-agent").Outcome);
    }

    [Fact]
    public async Task Ssh_agent_already_running_1056_ok()
    {
        var rig = new Rig();
        rig.Fingerprints($"256 {Fingerprint} x");
        rig.GitConfig(0, KnownTools.WindowsSsh);

        var items = await Run(rig);

        Assert.Empty(rig.Services.Calls);
        Assert.Equal(DevItemOutcome.AlreadyInOrder, Item(items, "ssh-agent").Outcome);
    }

    [Fact]
    public async Task Ssh_agent_start_failure_fails_the_item()
    {
        var rig = new Rig(sc: 1053);
        rig.GitConfig(0, KnownTools.WindowsSsh);

        var items = await Run(rig);

        Assert.Equal(DevItemOutcome.Failed, Item(items, "ssh-agent").Outcome);
        Assert.Equal(DevItemOutcome.NotApplicable, Item(items, "agent key").Outcome);
    }

    [Fact]
    public async Task Ssh_key_already_in_agent()
    {
        var rig = new Rig();
        rig.Fingerprints($"256 SHA256:other other (RSA){Environment.NewLine}256 {Fingerprint} joao@new-pc (ED25519)");
        rig.GitConfig(0, KnownTools.WindowsSsh);

        var items = await Run(rig);

        Assert.Equal(DevItemOutcome.AlreadyInOrder, Item(items, "agent key").Outcome);
        Assert.Empty(rig.Console.Attached);
        Assert.DoesNotContain(rig.Processes.Calls, c => c.FileName == Add && c.Arguments.Contains(KeyFile));
    }

    [Fact]
    public async Task Ssh_key_missing_from_agent_is_added_attached()
    {
        var rig = new Rig();
        rig.Fingerprints("The agent has no identities.", loadedExit: 1);
        rig.GitConfig(0, KnownTools.WindowsSsh);

        var items = await Run(rig);

        var attached = Assert.Single(rig.Console.Attached);
        Assert.Equal(Add, attached.FileName);
        Assert.Equal([KeyFile], attached.Arguments);
        Assert.Equal(DevItemOutcome.Done, Item(items, "agent key").Outcome);
    }

    [Fact]
    public async Task Ssh_sets_core_sshcommand()
    {
        var rig = new Rig();
        rig.Fingerprints($"256 {Fingerprint} x");
        rig.GitConfig(1);   // get: unset
        rig.GitConfig(0);   // set

        var items = await Run(rig);

        var gitCalls = rig.Processes.Calls.Where(c => c.FileName == GitExe).Select(c => string.Join(' ', c.Arguments)).ToArray();
        Assert.Equal(
        [
            "config --global --get core.sshCommand",
            "config --global core.sshCommand " + KnownTools.WindowsSsh,
        ], gitCalls);
        Assert.Equal(DevItemOutcome.Done, Item(items, "Git uses Windows SSH").Outcome);
    }

    [Fact]
    public async Task Ssh_without_git_marks_git_item_not_applicable()
    {
        var rig = new Rig(git: false);
        rig.Fingerprints($"256 {Fingerprint} x");

        var items = await Run(rig);

        Assert.Equal(DevItemOutcome.NotApplicable, Item(items, "Git uses Windows SSH").Outcome);
        Assert.DoesNotContain(rig.Processes.Calls, c => c.FileName == GitExe);
    }

    [Fact]
    public async Task Ssh_prints_public_key_and_link()
    {
        var rig = new Rig();
        rig.Fingerprints($"256 {Fingerprint} x");
        rig.GitConfig(0, KnownTools.WindowsSsh);

        var items = await Run(rig);

        Assert.Equal([PubKey, "https://github.com/settings/ssh/new"], rig.Console.Lines);
        var detail = Item(items, "key").Detail;
        Assert.Contains(PubKey, detail);
        Assert.Contains("https://github.com/settings/ssh/new", detail);
        Assert.DoesNotContain("PRIVATE", string.Join('\n', items.Select(i => i.Detail)));
    }

    [Fact]
    public async Task Ssh_never_runs_ssh_keygen_or_ssh_add_for_passphrases_through_the_runner()
    {
        var rig = new Rig(key: false);
        rig.Files.Seed(KeyFile + ".pub", PubKey);
        rig.Fingerprints("The agent has no identities.", loadedExit: 1);
        rig.GitConfig(0, KnownTools.WindowsSsh);

        await Run(rig);

        // Only the read-only listings are captured; creation and ssh-add <file> are attached.
        Assert.All(
            rig.Processes.Calls.Where(c => c.FileName is Keygen or Add),
            c => Assert.True(c.Arguments is ["-lf", _] or ["-l"]));
    }

    [Fact]
    public async Task Ssh_step_fails_on_pending_and_succeeds_otherwise()
    {
        var rig = new Rig();
        rig.Fingerprints($"256 {Fingerprint} x");
        rig.GitConfig(0, KnownTools.WindowsSsh);
        var step = new SshStep(Ed25519, Env());

        var result = await step.ExecuteAsync(rig.Context(), TestContext.Current.CancellationToken);

        Assert.Equal("dev.ssh", step.Id);
        Assert.True(step.RequiresAdmin);
        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
    }
}
