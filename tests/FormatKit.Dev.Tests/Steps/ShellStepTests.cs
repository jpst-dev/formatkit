using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Dev.Localization;
using FormatKit.Dev.Steps;
using static FormatKit.Dev.Tests.DevTestData;

namespace FormatKit.Dev.Tests.Steps;

public class ShellStepTests
{
    private const string OhMyPoshExe = @"C:\Users\Joao\AppData\Local\Microsoft\WindowsApps\oh-my-posh.exe";
    private const string Pwsh7Exe = @"C:\Program Files\PowerShell\7\pwsh.exe";
    private const string Pwsh7Profile = @"C:\Users\Joao\Documents\PowerShell\Microsoft.PowerShell_profile.ps1";
    private const string WinPsProfile = @"C:\Users\Joao\Documents\WindowsPowerShell\Microsoft.PowerShell_profile.ps1";
    private const string Fragment = @"C:\Users\Joao\AppData\Local\Microsoft\Windows Terminal\Fragments\FormatKit\formatkit.json";
    private const string Home = "/home/joao";
    private const string Omz = "/home/joao/.oh-my-zsh";
    private const string Zshrc = "/home/joao/.zshrc";

    private sealed class Rig
    {
        public FakeProcessRunner Processes { get; } = new();
        public FakeUserContextRunner UserContext { get; } = new();
        public FakeToolLocator Tools { get; } = new();
        public InMemoryFileSystem Files { get; } = new();

        public StepContext Context() =>
            TestServices.Context(TestServices.Create(processes: Processes, files: Files, tools: Tools, userContext: UserContext));

        public void Wsl(int exit, string stdout = "", string stderr = "") =>
            Processes.Enqueue("wsl.exe", new ProcessResult(exit, stdout, stderr, TimeSpan.Zero, false));

        /// <summary>The distro is listed and the user exists.</summary>
        public void Ready()
        {
            Wsl(0, "Ubuntu\r\n");
            Wsl(0, "1000");
        }

        public IEnumerable<string> Commands => Processes.Calls.Select(c => string.Join(' ', c.Arguments));

        /// <summary>The file written by WriteFileAsync: [..., "sh", path, content].</summary>
        public ProcessSpec? Write => Processes.Calls.SingleOrDefault(c => c.Arguments.Contains("sh"));
    }

    private static ShellStep Step(WslShellOptions? wsl = null, WindowsShellOptions? windows = null) =>
        new(new ShellOptions(wsl, windows), wsl is null ? null : new WslOptions("Ubuntu", "joao", null), Env());

    private static WslShellOptions Zsh(string theme = "robbyrussell", params string[] plugins) =>
        new(true, true, theme, plugins.Length == 0 ? ["git"] : plugins);

    private static WindowsShellOptions Win(string font = "Meslo") => new(true, "paradox", font);

    private static Task<StepResult> Run(ShellStep step, Rig rig) => step.ExecuteAsync(rig.Context(), TestContext.Current.CancellationToken);

    private const string Bash = "joao:x:1000:1000::/home/joao:/bin/bash\n";
    private const string ZshShell = "joao:x:1000:1000::/home/joao:/usr/bin/zsh\n";

    // ---- WSL side ----

    [Fact]
    public async Task Installs_zsh_and_oh_my_zsh_as_expected_users()
    {
        var rig = new Rig();
        rig.Ready();
        rig.Wsl(1);                                  // command -v zsh
        rig.Wsl(0);                                  // apt-get update
        rig.Wsl(0);                                  // apt-get install -y zsh
        rig.Wsl(0, "/usr/bin/zsh\n");                // command -v zsh
        rig.Wsl(1);                                  // test -d ~/.oh-my-zsh
        rig.Wsl(0, "/usr/bin/git\n");                // command -v git
        rig.Wsl(0);                                  // git clone
        rig.Wsl(1);                                  // cat ~/.zshrc: missing
        rig.Wsl(0, ZshRcTests.Template);             // cat template
        rig.Wsl(0);                                  // write ~/.zshrc
        rig.Wsl(0, Bash);                            // getent
        rig.Wsl(0);                                  // chsh

        var result = await Run(Step(Zsh("agnoster", "git", "z")), rig);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Equal(
        [
            "--list --quiet",
            "-d Ubuntu -u root --exec /usr/bin/id -u joao",
            "-d Ubuntu -u root --exec /bin/sh -c command -v zsh",
            "-d Ubuntu -u root --exec /usr/bin/apt-get update",
            "-d Ubuntu -u root --exec /usr/bin/apt-get install -y zsh",
            "-d Ubuntu -u root --exec /bin/sh -c command -v zsh",
            $"-d Ubuntu -u joao --exec /usr/bin/test -d {Omz}",
            "-d Ubuntu -u joao --exec /bin/sh -c command -v git",
            $"-d Ubuntu -u joao --exec /usr/bin/git clone --depth 1 https://github.com/ohmyzsh/ohmyzsh.git {Omz}",
            $"-d Ubuntu -u joao --exec /usr/bin/cat {Zshrc}",
            $"-d Ubuntu -u joao --exec /usr/bin/cat {Omz}/templates/zshrc.zsh-template",
            "WRITE",
            "-d Ubuntu -u root --exec /usr/bin/getent passwd joao",
            "-d Ubuntu -u root --exec /usr/bin/chsh -s /usr/bin/zsh joao",
        ], rig.Commands.Select(c => c.Contains("printf") ? "WRITE" : c));

        var write = rig.Write!;
        Assert.Equal(["-d", "Ubuntu", "-u", "joao", "--exec", "sh", "-c"], write.Arguments.Take(7));
        Assert.Equal(Zshrc, write.Arguments[^2]);
        Assert.Equal(ZshRc.Render(ZshRcTests.Template, "agnoster", ["git", "z"]), write.Arguments[^1]);
        Assert.All(result.Messages, m => Assert.Contains(Strings.Get("Outcome_Done"), m));
        Assert.Equal(TimeSpan.FromMinutes(30), rig.Processes.Calls[4].Timeout);
        Assert.Equal(TimeSpan.FromMinutes(30), rig.Processes.Calls[8].Timeout);
    }

    [Fact]
    public async Task Second_run_all_already_in_order()
    {
        var rig = new Rig();
        rig.Ready();
        rig.Wsl(0, "/usr/bin/zsh\n");
        rig.Wsl(0);                                  // test -d
        rig.Wsl(0, ZshRc.Render(ZshRcTests.Template, "robbyrussell", ["git"])); // cat ~/.zshrc
        rig.Wsl(0, ZshRcTests.Template);
        rig.Wsl(0, ZshShell);

        var result = await Run(Step(Zsh()), rig);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.All(result.Messages, m => Assert.Contains(Strings.Get("Outcome_AlreadyInOrder"), m));
        Assert.Equal(7, rig.Processes.Calls.Count);
        Assert.Null(rig.Write);
    }

    [Fact]
    public async Task External_plugins_cloned_from_allowlist()
    {
        var rig = new Rig();
        rig.Ready();
        rig.Wsl(0, "/usr/bin/zsh\n");
        rig.Wsl(0);                                  // test -d oh-my-zsh: present
        rig.Wsl(1);                                  // test -d autosuggestions: missing
        rig.Wsl(0, "/usr/bin/git\n");                // command -v git
        rig.Wsl(0);                                  // clone
        rig.Wsl(1);                                  // test -d syntax-highlighting: missing
        rig.Wsl(0);                                  // clone (git already known)
        rig.Wsl(0, "# user file\n");                 // cat ~/.zshrc: unmanaged
        rig.Wsl(0, ZshShell);

        var result = await Run(Step(Zsh("robbyrussell", "git", "zsh-autosuggestions", "zsh-syntax-highlighting")), rig);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Equal(
        [
            $"-d Ubuntu -u joao --exec /usr/bin/git clone --depth 1 https://github.com/zsh-users/zsh-autosuggestions.git {Omz}/custom/plugins/zsh-autosuggestions",
            $"-d Ubuntu -u joao --exec /usr/bin/git clone --depth 1 https://github.com/zsh-users/zsh-syntax-highlighting.git {Omz}/custom/plugins/zsh-syntax-highlighting",
        ], rig.Commands.Where(c => c.Contains("clone")));
        Assert.Single(rig.Commands, c => c.EndsWith("command -v git"));
        Assert.DoesNotContain(rig.Commands, c => c.Contains("custom/plugins/git"));
    }

    [Fact]
    public async Task Unmanaged_zshrc_kept()
    {
        var rig = new Rig();
        rig.Ready();
        rig.Wsl(0, "/usr/bin/zsh\n");
        rig.Wsl(0);
        rig.Wsl(0, "export MINE=1\n");               // cat ~/.zshrc: no marker
        rig.Wsl(0, ZshShell);

        var result = await Run(Step(Zsh()), rig);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.DoesNotContain(rig.Commands, c => c.Contains("zshrc.zsh-template"));
        Assert.Null(rig.Write);
        Assert.Contains(
            result.Messages,
            m => m.StartsWith(".zshrc: " + Strings.Get("Outcome_AlreadyInOrder")) && m.Contains(Strings.Get("Detail_ZshrcUnmanaged")));
    }

    [Fact]
    public async Task Managed_zshrc_rewritten_when_options_change()
    {
        var rig = new Rig();
        rig.Ready();
        rig.Wsl(0, "/usr/bin/zsh\n");
        rig.Wsl(0);
        rig.Wsl(0, ZshRc.Render(ZshRcTests.Template, "robbyrussell", ["git"])); // managed, old options
        rig.Wsl(0, ZshRcTests.Template);
        rig.Wsl(0);                                  // write
        rig.Wsl(0, ZshShell);

        var result = await Run(Step(Zsh("agnoster")), rig);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Equal(ZshRc.Render(ZshRcTests.Template, "agnoster", ["git"]), rig.Write!.Arguments[^1]);
        Assert.Contains(".zshrc: " + Strings.Get("Outcome_Done"), result.Messages);
    }

    [Fact]
    public async Task Login_shell_changed_once()
    {
        var rig = new Rig();
        rig.Ready();
        rig.Wsl(0, "/usr/bin/zsh\n");
        rig.Wsl(0);
        rig.Wsl(0, "# user file\n");
        rig.Wsl(0, Bash);
        rig.Wsl(0);                                  // chsh

        await Run(Step(Zsh()), rig);

        Assert.Single(rig.Commands, c => c.Contains("chsh"));
        Assert.Equal("-d Ubuntu -u root --exec /usr/bin/chsh -s /usr/bin/zsh joao", rig.Commands.Last());

        var again = new Rig();
        again.Ready();
        again.Wsl(0, "/usr/bin/zsh\n");
        again.Wsl(0);
        again.Wsl(0, "# user file\n");
        again.Wsl(0, ZshShell);

        await Run(Step(Zsh()), again);

        Assert.DoesNotContain(again.Commands, c => c.Contains("chsh"));
    }

    [Fact]
    public async Task Wsl_side_pending_without_user()
    {
        var rig = new Rig();
        rig.Wsl(0, "Ubuntu\r\n");
        rig.Wsl(1);                                  // id -u: no such user

        var result = await Run(Step(Zsh("robbyrussell", "git", "zsh-autosuggestions")), rig);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(Strings.Get("Next_RunWslUser"), result.NextAction);
        Assert.Equal(5, result.Messages.Count);      // zsh, Oh My Zsh, the plugin, .zshrc, login shell
        Assert.All(result.Messages, m => Assert.Contains(Strings.Get("Outcome_Pending"), m));
        Assert.Equal(2, rig.Processes.Calls.Count);
    }

    [Fact]
    public async Task Wsl_side_pending_without_distro()
    {
        var rig = new Rig();
        rig.Wsl(0, "Debian\r\n");

        var result = await Run(Step(Zsh()), rig);

        Assert.Equal(Strings.Get("Next_RunWslUser"), result.NextAction);
        Assert.Single(rig.Processes.Calls);
    }

    [Fact]
    public async Task Network_error_fails_with_network_hint_and_stops()
    {
        var rig = new Rig();
        rig.Ready();
        rig.Wsl(1);                                  // command -v zsh
        rig.Wsl(1, "", "WININET_E_NAME_NOT_RESOLVED"); // apt-get update

        var result = await Run(Step(Zsh()), rig);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(Strings.Get("Next_CheckNetwork"), result.NextAction);
        Assert.Equal(4, rig.Processes.Calls.Count);
    }

    [Fact]
    public async Task Virtualization_error_fails_with_bios_hint()
    {
        var rig = new Rig();
        rig.Wsl(0, "Ubuntu\r\n");
        rig.Wsl(1, "", "HCS_E_HYPERV_NOT_INSTALLED"); // id -u

        var result = await Run(Step(Zsh()), rig);

        Assert.Equal(Strings.Get("Next_EnableVirtualization"), result.NextAction);
    }

    [Fact]
    public async Task Network_error_reports_untried_wsl_items_and_still_runs_windows_side()
    {
        var rig = WithOhMyPosh();
        rig.Ready();
        rig.Wsl(1);                                  // command -v zsh
        rig.Wsl(1, "", "WININET_E_NAME_NOT_RESOLVED"); // apt-get update

        var result = await Run(Step(Zsh("robbyrussell", "git", "zsh-autosuggestions"), Win()), rig);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(Strings.Get("Next_CheckNetwork"), result.NextAction);
        Assert.Contains("zsh: " + Strings.Get("Outcome_Failed"), result.Messages);
        foreach (var label in new[] { "Oh My Zsh", "plugin zsh-autosuggestions", ".zshrc", "login shell" })
            Assert.Contains(label + ": " + Strings.Get("Outcome_NotApplicable"), result.Messages);
        Assert.Single(rig.UserContext.Calls);
        Assert.True(rig.Files.FileExists(WinPsProfile));
        Assert.True(rig.Files.FileExists(Fragment));
    }

    [Fact]
    public async Task Virtualization_error_at_clone_reports_rest_and_still_runs_windows_side()
    {
        var rig = WithOhMyPosh();
        rig.Ready();
        rig.Wsl(0, "/usr/bin/zsh\n");
        rig.Wsl(1);                                  // test -d
        rig.Wsl(0, "/usr/bin/git\n");
        rig.Wsl(1, "", "HCS_E_HYPERV_NOT_INSTALLED"); // clone

        var result = await Run(Step(Zsh(), Win()), rig);

        Assert.Equal(Strings.Get("Next_EnableVirtualization"), result.NextAction);
        Assert.Contains("zsh: " + Strings.Get("Outcome_AlreadyInOrder"), result.Messages);
        Assert.Contains("Oh My Zsh: " + Strings.Get("Outcome_Failed"), result.Messages);
        Assert.Contains(".zshrc: " + Strings.Get("Outcome_NotApplicable"), result.Messages);
        Assert.Contains("login shell: " + Strings.Get("Outcome_NotApplicable"), result.Messages);
        Assert.Single(rig.UserContext.Calls);
        Assert.True(rig.Files.FileExists(Fragment));
    }

    [Fact]
    public async Task Timed_out_user_check_is_failed_not_pending()
    {
        var rig = new Rig();
        rig.Wsl(0, "Ubuntu\r\n");
        rig.Processes.Enqueue("wsl.exe", new ProcessResult(-1, "", "", TimeSpan.Zero, true));

        var result = await Run(Step(Zsh()), rig);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(Strings.Get("Next_SeeLog"), result.NextAction);
        Assert.Contains("zsh: " + Strings.Get("Outcome_Failed"), result.Messages);
        Assert.DoesNotContain(result.Messages, m => m.Contains(Strings.Get("Outcome_Pending")));
    }

    [Fact]
    public async Task Failed_clone_fails_oh_my_zsh_and_skips_its_dependents()
    {
        var rig = new Rig();
        rig.Ready();
        rig.Wsl(0, "/usr/bin/zsh\n");
        rig.Wsl(1);                                  // test -d
        rig.Wsl(0, "/usr/bin/git\n");
        rig.Wsl(128, "", "fatal: unable to access"); // clone
        rig.Wsl(0, ZshShell);

        var result = await Run(Step(Zsh()), rig);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(Strings.Get("Next_SeeLog"), result.NextAction);
        Assert.Contains("Oh My Zsh: " + Strings.Get("Outcome_Failed"), result.Messages);
        Assert.Contains(".zshrc: " + Strings.Get("Outcome_NotApplicable"), result.Messages);
    }

    [Fact]
    public async Task Zsh_only_skips_oh_my_zsh_and_zshrc()
    {
        var rig = new Rig();
        rig.Ready();
        rig.Wsl(0, "/usr/bin/zsh\n");
        rig.Wsl(0, ZshShell);

        var result = await Run(Step(new WslShellOptions(true, false, "robbyrussell", ["git"])), rig);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Equal(2, result.Messages.Count);
    }

    // ---- Windows side ----

    private static Rig WithOhMyPosh()
    {
        var rig = new Rig();
        rig.Tools.Add("oh-my-posh.exe", OhMyPoshExe);
        rig.Files.CreateDirectory(@"C:\Users\Joao\Documents");
        return rig;
    }

    [Fact]
    public async Task OhMyPosh_missing_fails_with_add_hint()
    {
        var rig = new Rig();

        var result = await Run(Step(windows: Win()), rig);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(Strings.Get("Next_AddOhMyPosh"), result.NextAction);
        Assert.Equal(3, result.Messages.Count);      // font, Windows PowerShell profile, fragment
        Assert.All(result.Messages, m => Assert.Contains(Strings.Get("Outcome_Failed"), m));
        Assert.Empty(rig.UserContext.Calls);
    }

    [Fact]
    public async Task Installs_font_profiles_and_fragment()
    {
        var rig = WithOhMyPosh();
        rig.Files.Seed(Pwsh7Exe, "");

        var result = await Run(Step(windows: Win("JetBrainsMono")), rig);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        var font = Assert.Single(rig.UserContext.Calls);
        Assert.Equal(OhMyPoshExe, font.FileName);
        Assert.Equal(["font", "install", "jetbrainsmono"], font.Arguments);
        Assert.Contains("oh-my-posh init pwsh --config paradox | Invoke-Expression", rig.Files.ReadAllText(Pwsh7Profile));
        Assert.Contains("oh-my-posh init powershell --config paradox | Invoke-Expression", rig.Files.ReadAllText(WinPsProfile));
        Assert.Equal(TerminalFragment.Render("JetBrainsMono"), rig.Files.ReadAllText(Fragment));
        Assert.True(rig.Files.DirectoryExists(@"C:\Users\Joao\Documents\PowerShell"));
        Assert.All(result.Messages, m => Assert.Contains(Strings.Get("Outcome_Done"), m));
    }

    [Fact]
    public async Task Font_install_failure_is_reported()
    {
        var rig = WithOhMyPosh();
        rig.UserContext.Result = new ProcessResult(1, "", "", TimeSpan.Zero, false);

        var result = await Run(Step(windows: Win()), rig);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(Strings.Get("Next_SeeLog"), result.NextAction);
    }

    [Fact]
    public async Task Fragment_uses_family_name()
    {
        var rig = WithOhMyPosh();

        await Run(Step(windows: Win("Hack")), rig);

        Assert.Contains("\"face\": \"Hack Nerd Font\"", rig.Files.ReadAllText(Fragment));
    }

    [Fact]
    public async Task Profile_block_not_duplicated()
    {
        var rig = WithOhMyPosh();
        await Run(Step(windows: Win()), rig);
        var first = rig.Files.ReadAllText(WinPsProfile);
        var writes = rig.Files.WriteCount;

        var result = await Run(Step(windows: Win()), rig);

        Assert.Equal(first, rig.Files.ReadAllText(WinPsProfile));
        Assert.Single(first.Split("# >>> FormatKit oh-my-posh >>>"), s => s.Contains("oh-my-posh init"));
        Assert.Equal(writes, rig.Files.WriteCount);
        Assert.Contains(WinPsProfile + ": " + Strings.Get("Outcome_AlreadyInOrder"), result.Messages);
        Assert.Contains(Fragment + ": " + Strings.Get("Outcome_AlreadyInOrder"), result.Messages);
    }

    [Fact]
    public async Task Profile_with_existing_init_untouched()
    {
        var rig = WithOhMyPosh();
        const string Mine = "oh-my-posh init pwsh --config C:\\mine.omp.json | Invoke-Expression\r\n";
        rig.Files.Seed(WinPsProfile, Mine);

        var result = await Run(Step(windows: Win()), rig);

        Assert.Equal(Mine, rig.Files.ReadAllText(WinPsProfile));
        Assert.Contains(WinPsProfile + ": " + Strings.Get("Outcome_AlreadyInOrder"), result.Messages);
    }

    [Fact]
    public async Task Existing_profile_content_is_kept()
    {
        var rig = WithOhMyPosh();
        rig.Files.Seed(WinPsProfile, "Set-Alias a b");

        await Run(Step(windows: Win()), rig);

        var text = rig.Files.ReadAllText(WinPsProfile);
        Assert.StartsWith("Set-Alias a b\r\n", text);
        Assert.EndsWith("# <<< FormatKit oh-my-posh <<<\r\n", text);
    }

    [Fact]
    public async Task Pwsh7_profile_only_when_installed()
    {
        var without = WithOhMyPosh();
        await Run(Step(windows: Win()), without);
        Assert.False(without.Files.FileExists(Pwsh7Profile));
        Assert.True(without.Files.FileExists(WinPsProfile));

        var with = WithOhMyPosh();
        with.Files.Seed(Pwsh7Exe, "");
        await Run(Step(windows: Win()), with);
        Assert.True(with.Files.FileExists(Pwsh7Profile));
        Assert.True(with.Files.FileExists(WinPsProfile));
    }

    [Fact]
    public async Task Changed_fragment_is_rewritten()
    {
        var rig = WithOhMyPosh();
        rig.Files.Seed(Fragment, "{}");

        var result = await Run(Step(windows: Win("FiraCode")), rig);

        Assert.Equal(TerminalFragment.Render("FiraCode"), rig.Files.ReadAllText(Fragment));
        Assert.Contains(Fragment + ": " + Strings.Get("Outcome_Done"), result.Messages);
    }

    [Fact]
    public async Task OhMyPosh_off_adds_no_windows_items()
    {
        var rig = new Rig();

        var result = await Run(Step(windows: new WindowsShellOptions(false, "paradox", "Meslo")), rig);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Empty(result.Messages);
    }

    [Fact]
    public void Id_and_admin()
    {
        var step = Step();
        Assert.Equal("dev.shell", step.Id);
        Assert.False(step.RequiresAdmin);
    }
}
