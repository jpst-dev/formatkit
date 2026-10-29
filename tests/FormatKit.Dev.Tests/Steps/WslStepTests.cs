using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Dev.Localization;
using FormatKit.Dev.Steps;
using static FormatKit.Dev.Tests.DevTestData;

namespace FormatKit.Dev.Tests.Steps;

public class WslStepTests
{
    private const string Wslconfig = @"C:\Users\Joao\.wslconfig";

    private sealed class Rig
    {
        public FakeProcessRunner Processes { get; } = new();
        public FakeInteractiveConsole Console { get; } = new();
        public FakeUserSession Session { get; } = new();
        public InMemoryFileSystem Files { get; } = Profile();

        private static InMemoryFileSystem Profile()
        {
            var files = new InMemoryFileSystem();
            files.CreateDirectory(@"C:\Users\Joao");
            return files;
        }

        public StepContext Context() =>
            TestServices.Context(TestServices.Create(processes: Processes, files: Files, session: Session, console: Console));

        public void Wsl(int exit, string stdout = "", string stderr = "") =>
            Processes.Enqueue("wsl.exe", new ProcessResult(exit, stdout, stderr, TimeSpan.Zero, false));

        public IEnumerable<string> Commands => Processes.Calls.Select(c => string.Join(' ', c.Arguments));
    }

    private sealed class FixedTime(DateTimeOffset utc, TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utc;
        public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone("t", offset, "t", "t");
    }

    private static WslOptions Options(WslConfig? config = null) => new("Ubuntu", "joao", config);

    private static WslStep Step(WslOptions? options = null, TimeProvider? time = null) =>
        new(options ?? Options(), Env(), time ?? TimeProvider.System);

    [Fact]
    public async Task Installs_platform_and_requests_reboot()
    {
        var rig = new Rig();
        rig.Wsl(50); // --status
        rig.Wsl(0);  // --install --no-distribution

        var result = await Step().ExecuteAsync(rig.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.RebootRequired, result.Outcome);
        Assert.True(result.RunAgainAfterReboot);
        Assert.Contains("WSL: " + Strings.Get("Outcome_Done"), result.Messages);
        Assert.Equal(2, rig.Processes.Calls.Count);
    }

    [Fact]
    public async Task Second_run_all_already_in_order()
    {
        var rig = new Rig();
        rig.Wsl(0);                              // --status
        rig.Wsl(0, "Ubuntu\r\n");                // --list --quiet
        rig.Wsl(0, "1000");                      // id -u
        rig.Wsl(0, "joao P 2026-10-29 0 99999 7 -1"); // passwd -S
        rig.Wsl(0, "[boot]\nsystemd=true\n\n[user]\ndefault=joao\n"); // cat

        var result = await Step().ExecuteAsync(rig.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.All(result.Messages, m => Assert.Contains(Strings.Get("Outcome_AlreadyInOrder"), m));
        Assert.Empty(rig.Console.Attached);
        Assert.Equal(5, rig.Processes.Calls.Count);
    }

    [Fact]
    public async Task Creates_user_and_prompts_password()
    {
        var rig = new Rig();
        rig.Wsl(0);
        rig.Wsl(0, "Ubuntu");
        rig.Wsl(1);                              // id -u: missing
        rig.Wsl(0);                              // useradd
        rig.Wsl(0, "joao L 2026-10-29 0 99999 7 -1");
        rig.Wsl(0, "[boot]\nsystemd=true\n");    // cat
        rig.Wsl(0);                              // write wsl.conf
        rig.Wsl(0);                              // terminate

        var result = await Step().ExecuteAsync(rig.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        var attached = Assert.Single(rig.Console.Attached);
        Assert.Equal(["-d", "Ubuntu", "-u", "root", "--exec", "/usr/bin/passwd", "joao"], attached.Arguments);
        Assert.Contains("/usr/sbin/useradd -m -G sudo -s /bin/bash joao", rig.Commands.Single(c => c.Contains("useradd")));
        var write = rig.Processes.Calls.Single(c => c.Arguments.Contains("sh"));
        Assert.Equal("[boot]\nsystemd=true\n\n[user]\ndefault=joao\n", write.Arguments[^1]);
        Assert.Equal("--terminate Ubuntu", rig.Commands.Last());
    }

    [Fact]
    public async Task Nobody_at_pc_leaves_password_pending_and_never_attaches()
    {
        var rig = new Rig { Console = { CanPrompt = false } };
        rig.Wsl(0);
        rig.Wsl(0, "Ubuntu");
        rig.Wsl(1);
        rig.Wsl(0);
        rig.Wsl(0, "joao L 2026-10-29 0 99999 7 -1");
        rig.Wsl(1);                              // cat: no wsl.conf
        rig.Wsl(0);
        rig.Wsl(0);

        var result = await Step().ExecuteAsync(rig.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(Strings.Get("Next_RunWslUser"), result.NextAction);
        Assert.Empty(rig.Console.Attached);
    }

    [Fact]
    public async Task Wrong_account_fails_without_running_wsl()
    {
        var rig = new Rig { Session = { IsConsoleUser = false } };

        var result = await Step(Options(new WslConfig("8GB", null, null))).ExecuteAsync(rig.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(Strings.Get("Next_WrongAccount"), result.NextAction);
        Assert.Equal(4, result.Messages.Count);
        Assert.Empty(rig.Processes.Calls);
    }

    [Fact]
    public async Task Virtualization_error_fails_with_bios_hint()
    {
        var rig = new Rig();
        rig.Wsl(0);
        rig.Wsl(0, "Ubuntu");
        rig.Wsl(-1, "", "Código de erro: Wsl/Service/CreateInstance/HCS_E_HYPERV_NOT_INSTALLED"); // id -u

        var result = await Step(Options(new WslConfig("8GB", null, null))).ExecuteAsync(rig.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(Strings.Get("Next_EnableVirtualization"), result.NextAction);
        Assert.Equal(3, rig.Processes.Calls.Count);
        Assert.Empty(rig.Files.Files);
    }

    [Fact]
    public async Task Network_error_on_distro_install_fails_and_stops()
    {
        var rig = new Rig();
        rig.Wsl(0);
        rig.Wsl(0, "");
        rig.Wsl(-1, "", "Código de erro: Wsl/InstallDistro/WININET_E_NAME_NOT_RESOLVED");

        var result = await Step().ExecuteAsync(rig.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(Strings.Get("Next_CheckNetwork"), result.NextAction);
        Assert.Equal(3, rig.Processes.Calls.Count);
    }

    [Fact]
    public async Task Platform_install_failure_points_to_the_log()
    {
        var rig = new Rig();
        rig.Wsl(50);
        rig.Wsl(-1, "", "boom");

        var result = await Step().ExecuteAsync(rig.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(Strings.Get("Next_SeeLog"), result.NextAction);
    }

    [Fact]
    public async Task Wslconfig_is_written_when_missing()
    {
        var rig = new Rig();
        EnqueueInOrder(rig);

        var result = await Step(Options(new WslConfig("8GB", 4, null))).ExecuteAsync(rig.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Equal("[wsl2]\nmemory=8GB\nprocessors=4\n", rig.Files.ReadAllText(Wslconfig));
    }

    [Fact]
    public async Task Wslconfig_backup_name_uses_local_time()
    {
        var rig = new Rig();
        rig.Files.Seed(Wslconfig, "[wsl2]\nmemory=2GB\n");
        EnqueueInOrder(rig);
        var time = new FixedTime(new DateTimeOffset(2026, 10, 28, 23, 30, 5, TimeSpan.Zero), TimeSpan.FromHours(3));

        var result = await Step(Options(new WslConfig("8GB", null, null)), time).ExecuteAsync(rig.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Equal("[wsl2]\nmemory=2GB\n", rig.Files.ReadAllText(Wslconfig + ".formatkit-20261029-023005.bak"));
        Assert.Equal("[wsl2]\nmemory=8GB\n", rig.Files.ReadAllText(Wslconfig));
    }

    [Fact]
    public async Task Identical_wslconfig_is_left_alone()
    {
        var rig = new Rig();
        rig.Files.Seed(Wslconfig, "[wsl2]\nmemory=8GB\n");
        EnqueueInOrder(rig);

        await Step(Options(new WslConfig("8GB", null, null))).ExecuteAsync(rig.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(0, rig.Files.WriteCount);
        Assert.Single(rig.Files.Files);
    }

    [Fact]
    public async Task EnsureUser_is_idempotent_when_password_set()
    {
        var rig = new Rig();
        rig.Wsl(0, "1000");
        rig.Wsl(0, "joao P 2026-10-29 0 99999 7 -1");
        rig.Wsl(0, "[user]\ndefault=joao\n");

        var item = await WslStep.EnsureUserAsync(Options(), rig.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(DevItemOutcome.AlreadyInOrder, item.Outcome);
        Assert.Empty(rig.Console.Attached);
        Assert.Equal(3, rig.Processes.Calls.Count);
    }

    [Fact]
    public async Task EnsureUser_prompts_when_password_is_locked_and_user_exists()
    {
        var rig = new Rig();
        rig.Wsl(0, "1000");
        rig.Wsl(0, "joao L 2026-10-29 0 99999 7 -1");
        rig.Wsl(0, "[user]\ndefault=joao\n");

        var item = await WslStep.EnsureUserAsync(Options(), rig.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(DevItemOutcome.Done, item.Outcome);
        Assert.Single(rig.Console.Attached);
    }

    [Fact]
    public async Task EnsureUser_fails_for_wrong_account_without_running_wsl()
    {
        var rig = new Rig { Session = { IsConsoleUser = false } };

        var item = await WslStep.EnsureUserAsync(Options(), rig.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(DevItemOutcome.Failed, item.Outcome);
        Assert.Equal(Strings.Get("Next_WrongAccount"), item.NextAction);
        Assert.Empty(rig.Processes.Calls);
    }

    [Fact]
    public async Task Cat_timeout_fails_without_writing_wsl_conf()
    {
        var rig = new Rig();
        rig.Wsl(0, "1000");
        rig.Wsl(0, "joao P 2026-10-29 0 99999 7 -1");
        rig.Processes.Enqueue("wsl.exe", new ProcessResult(-1, "", "", TimeSpan.Zero, true));

        var item = await WslStep.EnsureUserAsync(Options(), rig.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(DevItemOutcome.Failed, item.Outcome);
        Assert.Equal(Strings.Get("Next_SeeLog"), item.NextAction);
        Assert.Equal(3, rig.Processes.Calls.Count);
    }

    [Fact]
    public async Task Cat_wsl_failure_fails_without_writing_wsl_conf()
    {
        var rig = new Rig();
        rig.Wsl(0, "1000");
        rig.Wsl(0, "joao P 2026-10-29 0 99999 7 -1");
        rig.Wsl(-1, "", "boom");

        var item = await WslStep.EnsureUserAsync(Options(), rig.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(DevItemOutcome.Failed, item.Outcome);
        Assert.Equal(Strings.Get("Next_SeeLog"), item.NextAction);
        Assert.Equal(3, rig.Processes.Calls.Count);
    }

    private static void EnqueueInOrder(Rig rig)
    {
        rig.Wsl(0);
        rig.Wsl(0, "Ubuntu");
        rig.Wsl(0, "1000");
        rig.Wsl(0, "joao P 2026-10-29 0 99999 7 -1");
        rig.Wsl(0, "[user]\ndefault=joao\n");
    }
}
