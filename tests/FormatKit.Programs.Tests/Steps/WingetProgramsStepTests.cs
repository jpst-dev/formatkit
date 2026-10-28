using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Programs.Catalog;
using FormatKit.Programs.Steps;

namespace FormatKit.Programs.Tests.Steps;

public class WingetProgramsStepTests : IDisposable
{
    private readonly System.Globalization.CultureInfo _original = System.Globalization.CultureInfo.CurrentUICulture;

    public WingetProgramsStepTests() => System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("en-US");

    public void Dispose() => System.Globalization.CultureInfo.CurrentUICulture = _original;

    private static readonly ProcessResult Ok = new(0, "", "", TimeSpan.Zero, false);

    private static ProcessResult Exit(int code) => new(code, "", "", TimeSpan.Zero, false);

    private static SelectedProgram Program(string name, InstallContext context = InstallContext.Machine) =>
        new($"winget:winget:{name}", name, null, new WingetEntry(name, "winget", null, context));

    private static WingetProgramsStep Step(bool upgrade, params SelectedProgram[] programs) =>
        new("programs.other", "Programs: other", programs, upgrade);

    private static (FakeProcessRunner Runner, StepContext Ctx, FakeUserContextRunner User, FakeUserSession Session) Setup()
    {
        var runner = new FakeProcessRunner();
        var user = new FakeUserContextRunner();
        var session = new FakeUserSession();
        var ctx = TestServices.Context(TestServices.Create(processes: runner, session: session, userContext: user));
        return (runner, ctx, user, session);
    }

    [Fact]
    public async Task Failure_of_one_program_does_not_stop_others()
    {
        var (runner, ctx, _, _) = Setup();
        runner.Enqueue("winget.exe", Ok);                   // --version
        runner.Enqueue("winget.exe", Exit(1));              // list A: not installed
        runner.Enqueue("winget.exe", Exit(unchecked((int)0x8A150014))); // install A: not found
        runner.Enqueue("winget.exe", Exit(1));              // list B
        runner.Enqueue("winget.exe", Ok);                   // install B

        var result = await Step(false, Program("A.A"), Program("B.B")).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.NotNull(result.NextAction);
        Assert.Equal(2, result.Messages.Count);
        Assert.StartsWith("A.A: ", result.Messages[0]);
        Assert.StartsWith("B.B: ", result.Messages[1]);
        Assert.Contains("installed", result.Messages[1]);
    }

    [Fact]
    public async Task Installed_program_is_already_installed_without_upgrade()
    {
        var (runner, ctx, _, _) = Setup();
        runner.Enqueue("winget.exe", Ok);
        runner.Enqueue("winget.exe", Ok); // list: installed

        var result = await Step(false, Program("A.A")).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Contains("already installed", result.Messages[0]);
        Assert.DoesNotContain(runner.Calls, c => c.Arguments[0] is "install" or "upgrade");
    }

    [Fact]
    public async Task Upgrade_flag_upgrades_installed_program()
    {
        var (runner, ctx, _, _) = Setup();
        runner.Enqueue("winget.exe", Ok);
        runner.Enqueue("winget.exe", Ok); // list: installed
        runner.Enqueue("winget.exe", Ok); // upgrade

        var result = await Step(true, Program("A.A")).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Contains("upgraded", result.Messages[0]);
        Assert.Contains(runner.Calls, c => c.Arguments[0] == "upgrade");
    }

    [Fact]
    public async Task Reboot_required_item_makes_step_reboot_after_all_items()
    {
        var (runner, ctx, _, _) = Setup();
        runner.Enqueue("winget.exe", Ok);
        runner.Enqueue("winget.exe", Exit(1));
        runner.Enqueue("winget.exe", Exit(unchecked((int)0x8A150109))); // reboot required
        runner.Enqueue("winget.exe", Exit(1));
        runner.Enqueue("winget.exe", Ok);

        var result = await Step(false, Program("A.A"), Program("B.B")).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.RebootRequired, result.Outcome);
        Assert.Equal(2, result.Messages.Count);
    }

    [Fact]
    public async Task Winget_missing_registers_once_then_fails_all_items_with_next_action()
    {
        var (runner, ctx, _, _) = Setup();
        runner.ThrowNotFound("winget.exe");
        runner.Enqueue("powershell.exe", Ok);

        var result = await Step(false, Program("A.A"), Program("B.B")).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(2, result.Messages.Count);
        Assert.Contains("winget", result.NextAction, StringComparison.OrdinalIgnoreCase);
        var register = Assert.Single(runner.Calls, c => c.FileName == "powershell.exe");
        Assert.Contains("Microsoft.DesktopAppInstaller_8wekyb3d8bbwe", register.Arguments[^1]);
    }

    [Fact]
    public async Task Winget_registered_on_retry_continues()
    {
        var (runner, ctx, _, _) = Setup();
        runner.Enqueue("winget.exe", Exit(1)); // --version fails the first time
        runner.Enqueue("powershell.exe", Ok);
        runner.Enqueue("winget.exe", Ok);      // --version works now
        runner.Enqueue("winget.exe", Exit(1)); // list
        runner.Enqueue("winget.exe", Ok);      // install

        var result = await Step(false, Program("A.A")).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
    }

    [Fact]
    public async Task User_context_item_runs_through_user_runner()
    {
        var (runner, ctx, user, _) = Setup();
        runner.Enqueue("winget.exe", Ok); // --version

        var result = await Step(false, Program("U.U", InstallContext.User)).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        var call = Assert.Single(user.Calls);
        Assert.Equal(@"%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe", call.FileName);
        Assert.Equal("install", call.Arguments[0]);
        Assert.Single(runner.Calls); // only --version ran as the service
    }

    [Fact]
    public async Task Package_scope_is_passed_to_install()
    {
        var (runner, ctx, user, _) = Setup();
        runner.Enqueue("winget.exe", Ok);      // --version
        runner.Enqueue("winget.exe", Exit(1)); // list: not installed
        runner.Enqueue("winget.exe", Ok);      // install
        var machine = new SelectedProgram("m", "M.M", null, new WingetEntry("M.M", "winget", null, InstallContext.Machine, "machine"));
        var perUser = new SelectedProgram("u", "U.U", null, new WingetEntry("U.U", "winget", null, InstallContext.User, "user"));

        await Step(false, machine, perUser).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("--scope", runner.Calls.Single(c => c.Arguments[0] == "list").Arguments);
        Assert.Equal(["--scope", "machine"], runner.Calls.Single(c => c.Arguments[0] == "install").Arguments.TakeLast(2));
        Assert.Equal(["--scope", "user"], Assert.Single(user.Calls).Arguments.TakeLast(2));
    }

    [Fact]
    public async Task Entry_without_scope_passes_no_scope()
    {
        var (runner, ctx, _, _) = Setup();
        runner.Enqueue("winget.exe", Ok);
        runner.Enqueue("winget.exe", Exit(1));
        runner.Enqueue("winget.exe", Ok);

        await Step(false, Program("A.A")).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("--scope", runner.Calls.Single(c => c.Arguments[0] == "install").Arguments);
    }

    [Fact]
    public async Task User_context_item_without_signed_in_user_fails_with_sign_in_action()
    {
        var (runner, ctx, user, session) = Setup();
        session.HasInteractiveUser = false;
        runner.Enqueue("winget.exe", Ok);

        var result = await Step(false, Program("U.U", InstallContext.User)).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Contains("Sign in", result.NextAction);
        Assert.Empty(user.Calls);
        Assert.Contains("no user", result.Messages[0]);
    }

    [Fact]
    public async Task Timed_out_install_fails_item_and_next_runs()
    {
        var (runner, ctx, _, _) = Setup();
        runner.Enqueue("winget.exe", Ok);
        runner.Enqueue("winget.exe", Exit(1));
        runner.Enqueue("winget.exe", new ProcessResult(-1, "", "", TimeSpan.Zero, true));
        runner.Enqueue("winget.exe", Exit(1));
        runner.Enqueue("winget.exe", Ok);

        var result = await Step(false, Program("A.A"), Program("B.B")).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Contains("timed out", result.Messages[0]);
        Assert.Contains("installed", result.Messages[1]);
    }

    private sealed class CancellingRunner(CancellationTokenSource cts) : IUserContextRunner
    {
        public int Calls { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct)
        {
            Calls++;
            cts.Cancel();
            return Task.FromResult(new ProcessResult(0, "", "", TimeSpan.Zero, false));
        }
    }

    [Fact]
    public async Task Cancellation_between_items_stops_after_current()
    {
        using var cts = new CancellationTokenSource();
        var runner = new FakeProcessRunner();
        var user = new CancellingRunner(cts);
        var ctx = TestServices.Context(TestServices.Create(processes: runner, userContext: user));
        runner.Enqueue("winget.exe", Ok);

        var step = Step(false, Program("U.One", InstallContext.User), Program("U.Two", InstallContext.User));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => step.ExecuteAsync(ctx, cts.Token));

        Assert.Equal(1, user.Calls);
    }

    [Fact]
    public async Task Per_item_winget_calls_get_a_token_that_ctrl_c_cannot_cancel()
    {
        using var cts = new CancellationTokenSource();
        var (runner, ctx, user, _) = Setup();
        runner.Enqueue("winget.exe", Ok);      // --version
        runner.Enqueue("winget.exe", Ok);      // list A: installed
        runner.Enqueue("winget.exe", Ok);      // upgrade A
        runner.Enqueue("winget.exe", Exit(1)); // list B: not installed
        runner.Enqueue("winget.exe", Ok);      // install B

        await Step(true, Program("A.A"), Program("B.B"), Program("U.U", InstallContext.User)).ExecuteAsync(ctx, cts.Token);

        var perItem = runner.Calls.Select((c, i) => (c, i)).Where(x => x.c.Arguments[0] is "list" or "install" or "upgrade").ToList();
        Assert.Equal(4, perItem.Count);
        Assert.All(perItem, x => Assert.False(runner.Tokens[x.i].CanBeCanceled));
        Assert.NotEmpty(user.Tokens);
        Assert.All(user.Tokens, t => Assert.False(t.CanBeCanceled));
    }

    [Fact]
    public async Task Ctrl_c_during_an_install_lets_it_finish_then_stops_before_the_next()
    {
        using var cts = new CancellationTokenSource();
        var (runner, ctx, _, _) = Setup();
        runner.Enqueue("winget.exe", Ok);      // --version
        runner.Enqueue("winget.exe", Exit(1)); // list A
        runner.Enqueue("winget.exe", Ok);      // install A
        runner.OnRun = spec =>
        {
            if (spec.Arguments[0] == "install")
                cts.Cancel();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Step(false, Program("A.A"), Program("B.B")).ExecuteAsync(ctx, cts.Token));

        Assert.Single(runner.Calls, c => c.Arguments[0] == "install");
        Assert.DoesNotContain(runner.Calls, c => c.Arguments.Contains("B.B"));
    }

    [Fact]
    public async Task User_item_install_passes_no_upgrade()
    {
        var (runner, ctx, user, _) = Setup();
        runner.Enqueue("winget.exe", Ok);

        await Step(false, Program("U.U", InstallContext.User)).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Contains("--no-upgrade", Assert.Single(user.Calls).Arguments);
    }

    [Fact]
    public async Task User_item_already_installed_without_upgrade_is_reported_and_not_upgraded()
    {
        var runner = new FakeProcessRunner();
        var user = new SequenceRunner(unchecked((int)0x8A150061));
        var ctx = TestServices.Context(TestServices.Create(processes: runner, userContext: user));
        runner.Enqueue("winget.exe", Ok);

        var result = await Step(false, Program("U.U", InstallContext.User)).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Contains("already installed", result.Messages[0]);
        Assert.Equal("install", Assert.Single(user.Calls).Arguments[0]);
    }

    [Fact]
    public async Task User_item_without_signed_in_user_says_failed()
    {
        var (runner, ctx, _, session) = Setup();
        session.HasInteractiveUser = false;
        runner.Enqueue("winget.exe", Ok);

        var result = await Step(false, Program("U.U", InstallContext.User)).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal("U.U: failed: no user is signed in", result.Messages[0]);
        Assert.Equal("Sign in to Windows and run again.", result.NextAction);
    }

    [Fact]
    public async Task User_item_with_upgrade_and_not_installed_installs_once()
    {
        var (runner, ctx, user, _) = Setup();
        runner.Enqueue("winget.exe", Ok);

        var result = await Step(true, Program("U.U", InstallContext.User)).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Contains("installed", result.Messages[0]);
        Assert.Equal("install", Assert.Single(user.Calls).Arguments[0]);
    }

    private sealed class SequenceRunner(params int[] codes) : IUserContextRunner
    {
        public List<ProcessSpec> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct)
        {
            Calls.Add(spec);
            return Task.FromResult(new ProcessResult(codes[Calls.Count - 1], "", "", TimeSpan.Zero, false));
        }
    }

    [Fact]
    public async Task User_item_with_upgrade_and_already_installed_installs_then_upgrades()
    {
        var runner = new FakeProcessRunner();
        var user = new SequenceRunner(unchecked((int)0x8A150061), 0);
        var ctx = TestServices.Context(TestServices.Create(processes: runner, userContext: user));
        runner.Enqueue("winget.exe", Ok);

        var result = await Step(true, Program("U.U", InstallContext.User)).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Contains("upgraded", result.Messages[0]);
        Assert.Equal(["install", "upgrade"], user.Calls.Select(c => c.Arguments[0]));
    }

    [Fact]
    public async Task Check_true_only_when_all_installed_and_no_upgrade()
    {
        var (runner, ctx, _, _) = Setup();
        runner.Enqueue("winget.exe", Ok);
        runner.Enqueue("winget.exe", Ok);
        Assert.True(await Step(false, Program("A.A"), Program("B.B")).CheckAsync(ctx, TestContext.Current.CancellationToken));

        runner.Enqueue("winget.exe", Ok);
        runner.Enqueue("winget.exe", Exit(1));
        Assert.False(await Step(false, Program("A.A"), Program("B.B")).CheckAsync(ctx, TestContext.Current.CancellationToken));

        Assert.False(await Step(true, Program("A.A")).CheckAsync(ctx, TestContext.Current.CancellationToken));
    }
}
