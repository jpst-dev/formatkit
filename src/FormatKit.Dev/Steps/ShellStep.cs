using System.Text;
using FormatKit.Core;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Dev.Localization;

namespace FormatKit.Dev.Steps;

/// <summary>zsh and Oh My Zsh inside WSL; Oh My Posh, its font and the Windows Terminal font on Windows (spec 6.5).</summary>
internal sealed class ShellStep(ShellOptions options, WslOptions? wsl, DevEnvironment env) : IStep
{
    private const string ZshLabel = "zsh";
    private const string OhMyZshLabel = "Oh My Zsh";
    private const string ZshrcLabel = ".zshrc";
    private const string LoginShellLabel = "login shell";
    private const string OhMyZshRepository = "https://github.com/ohmyzsh/ohmyzsh.git";
    private const string PluginRepositoryRoot = "https://github.com/zsh-users/";

    private static readonly UTF8Encoding Utf8 = new(false);

    public string Id => "dev.shell";

    public string Title => Strings.Get("Step_Title_Shell");

    public bool RequiresAdmin => false;

    public bool IsCritical => false;

    public Task<bool> CheckAsync(StepContext ctx, CancellationToken ct) => Task.FromResult(false);

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var items = new List<DevItem>();

        if (options.Wsl is { Zsh: true } shell && wsl is not null)
            await RunWslAsync(shell, wsl, items, ctx, ct).ConfigureAwait(false);

        if (options.Windows is { OhMyPosh: true } windows)
            await RunWindowsAsync(windows, items, ctx, ct).ConfigureAwait(false);

        return DevStepResult.From(items);
    }

    // ---- WSL side ----

    /// <summary>
    /// Adds one item per WSL step. Whatever stops the WSL side early (a virtualization or network error, a failed install),
    /// the items not tried are reported NotApplicable so none vanishes, and the Windows side still runs.
    /// </summary>
    private async Task RunWslAsync(WslShellOptions shell, WslOptions target, List<DevItem> items, StepContext ctx, CancellationToken ct)
    {
        await RunWslCoreAsync(shell, target, items, ctx, ct).ConfigureAwait(false);

        List<string> planned = [ZshLabel];
        if (shell.OhMyZsh)
        {
            planned.Add(OhMyZshLabel);
            planned.AddRange(shell.Plugins.Where(p => DevOptions.ExternalPlugins.Contains(p)).Select(p => $"plugin {p}"));
            planned.Add(ZshrcLabel);
        }

        planned.Add(LoginShellLabel);
        foreach (var label in planned.Where(l => items.All(i => i.Label != l)))
            items.Add(new(label, DevItemOutcome.NotApplicable));
    }

    /// <summary>Runs the WSL items in order; true when a virtualization or network failure stopped it.</summary>
    private async Task<bool> RunWslCoreAsync(WslShellOptions shell, WslOptions target, List<DevItem> items, StepContext ctx, CancellationToken ct)
    {
        var client = ctx.Services.Wsl;
        var home = $"/home/{target.User}";
        var omz = $"{home}/.oh-my-zsh";
        var externals = shell.OhMyZsh ? shell.Plugins.Where(p => DevOptions.ExternalPlugins.Contains(p)).ToArray() : [];

        // Distro or user missing: nothing to do yet, the user is created by `formatkit dev wsl-user` or the wsl step.
        var ready = (await ListDistrosAsync(client, ct).ConfigureAwait(false)).Contains(target.Distro, StringComparer.OrdinalIgnoreCase);
        if (ready)
        {
            var id = await client.ExecAsync(target.Distro, "root", ["/usr/bin/id", "-u", target.User], ct).ConfigureAwait(false);
            if (IsFatal(id))
            {
                items.Add(Failure(ZshLabel, id, ctx));
                return true;
            }

            // Only a plain exit 1 means the user does not exist; a timeout or a wsl.exe failure is not "missing".
            if (id.TimedOut || id.ExitCode is < 0 or > 1)
            {
                items.Add(Failure(ZshLabel, id, ctx));
                return false;
            }

            ready = id is { ExitCode: 0, TimedOut: false };
        }

        if (!ready)
        {
            var pending = Strings.Get("Next_RunWslUser");
            items.Add(new(ZshLabel, DevItemOutcome.Pending, pending));
            if (shell.OhMyZsh)
            {
                items.Add(new(OhMyZshLabel, DevItemOutcome.Pending, pending));
                items.AddRange(externals.Select(p => new DevItem($"plugin {p}", DevItemOutcome.Pending, pending)));
                items.Add(new(ZshrcLabel, DevItemOutcome.Pending, pending));
            }

            items.Add(new(LoginShellLabel, DevItemOutcome.Pending, pending));
            return false;
        }

        var zsh = await EnsureZshAsync(target, ctx, ct).ConfigureAwait(false);
        items.Add(zsh.Item);
        if (zsh.Path is null)
            return IsFatal(zsh.Last);

        if (shell.OhMyZsh)
        {
            var git = new GitState();
            var omzItem = await EnsureCloneAsync(OhMyZshLabel, target, omz, OhMyZshRepository, git, ctx, ct).ConfigureAwait(false);
            items.Add(omzItem.Item);
            if (omzItem.Fatal)
                return true;

            if (omzItem.Item.Outcome == DevItemOutcome.Failed)
            {
                items.AddRange(externals.Select(p => new DevItem($"plugin {p}", DevItemOutcome.NotApplicable)));
                items.Add(new(ZshrcLabel, DevItemOutcome.NotApplicable));
            }
            else
            {
                foreach (var plugin in externals)
                {
                    var item = await EnsureCloneAsync(
                        $"plugin {plugin}", target, $"{omz}/custom/plugins/{plugin}",
                        $"{PluginRepositoryRoot}{plugin}.git", git, ctx, ct).ConfigureAwait(false);
                    items.Add(item.Item);
                    if (item.Fatal)
                        return true;
                }

                items.Add(await EnsureZshrcAsync(shell, target, home, omz, ctx, ct).ConfigureAwait(false));
            }
        }

        items.Add(await EnsureLoginShellAsync(target, zsh.Path, ctx, ct).ConfigureAwait(false));
        return false;
    }

    private static async Task<IReadOnlyList<string>> ListDistrosAsync(WslClient client, CancellationToken ct)
    {
        try
        {
            return await client.ListDistrosAsync(ct).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return [];
        }
    }

    private static async Task<ZshResult> EnsureZshAsync(WslOptions target, StepContext ctx, CancellationToken ct)
    {
        var wsl = ctx.Services.Wsl;
        var found = await wsl.ExecAsync(target.Distro, "root", ["/bin/sh", "-c", "command -v zsh"], ct).ConfigureAwait(false);
        if (found is { ExitCode: 0, TimedOut: false })
            return new(new(ZshLabel, DevItemOutcome.AlreadyInOrder), found.StdOut.Trim(), found);

        foreach (var command in new[] { new[] { "update" }, ["install", "-y", "zsh"] })
        {
            var apt = await wsl.ExecAsync(target.Distro, "root", ["/usr/bin/apt-get", .. command], ct, Timeouts.Install).ConfigureAwait(false);
            if (apt.ExitCode != 0 || apt.TimedOut)
                return new(Failure(ZshLabel, apt, ctx), null, apt);
        }

        found = await wsl.ExecAsync(target.Distro, "root", ["/bin/sh", "-c", "command -v zsh"], ct).ConfigureAwait(false);
        if (found is { ExitCode: 0, TimedOut: false })
            return new(new(ZshLabel, DevItemOutcome.Done), found.StdOut.Trim(), found);
        return new(Failure(ZshLabel, found, ctx), null, found);
    }

    /// <summary>Clones <paramref name="repository"/> into <paramref name="path"/> as the user unless the folder exists; git comes from apt when missing.</summary>
    private static async Task<CloneResult> EnsureCloneAsync(
        string label, WslOptions target, string path, string repository, GitState git, StepContext ctx, CancellationToken ct)
    {
        var wsl = ctx.Services.Wsl;
        var exists = await wsl.ExecAsync(target.Distro, target.User, ["/usr/bin/test", "-d", path], ct).ConfigureAwait(false);
        if (exists is { ExitCode: 0, TimedOut: false })
            return new(new(label, DevItemOutcome.AlreadyInOrder), false);

        if (!git.Checked)
        {
            var failure = await EnsureGitAsync(target, ctx, ct).ConfigureAwait(false);
            if (failure is not null)
                return new(Failure(label, failure, ctx), IsFatal(failure));
            git.Checked = true;
        }

        var clone = await wsl.ExecAsync(
            target.Distro, target.User, ["/usr/bin/git", "clone", "--depth", "1", repository, path], ct, Timeouts.Install).ConfigureAwait(false);
        if (clone.ExitCode != 0 || clone.TimedOut)
            return new(Failure(label, clone, ctx), IsFatal(clone));
        return new(new(label, DevItemOutcome.Done), false);
    }

    /// <summary>Null when git is there (installed now if needed); otherwise the failing result.</summary>
    private static async Task<ProcessResult?> EnsureGitAsync(WslOptions target, StepContext ctx, CancellationToken ct)
    {
        var wsl = ctx.Services.Wsl;
        var found = await wsl.ExecAsync(target.Distro, target.User, ["/bin/sh", "-c", "command -v git"], ct).ConfigureAwait(false);
        if (found is { ExitCode: 0, TimedOut: false })
            return null;

        foreach (var command in new[] { new[] { "update" }, ["install", "-y", "git"] })
        {
            var apt = await wsl.ExecAsync(target.Distro, "root", ["/usr/bin/apt-get", .. command], ct, Timeouts.Install).ConfigureAwait(false);
            if (apt.ExitCode != 0 || apt.TimedOut)
                return apt;
        }

        return null;
    }

    private static async Task<DevItem> EnsureZshrcAsync(
        WslShellOptions shell, WslOptions target, string home, string omz, StepContext ctx, CancellationToken ct)
    {
        var wsl = ctx.Services.Wsl;
        var path = $"{home}/.zshrc";

        // Only a plain exit 1 means the file is missing; anything else must not look like an empty file.
        var cat = await wsl.ExecAsync(target.Distro, target.User, ["/usr/bin/cat", path], ct).ConfigureAwait(false);
        if (cat.TimedOut || cat.ExitCode is < 0 or > 1)
            return Failure(ZshrcLabel, cat, ctx);

        var existing = cat.ExitCode == 0 ? cat.StdOut.Replace("\r\n", "\n") : "";
        if (existing.Trim().Length > 0 && !ZshRc.IsManaged(existing))
        {
            ctx.Log.Warning("{Path} was not written by FormatKit and is left as it is", path);
            return new(ZshrcLabel, DevItemOutcome.AlreadyInOrder, null, Strings.Get("Detail_ZshrcUnmanaged"));
        }

        var template = await wsl.ExecAsync(target.Distro, target.User, ["/usr/bin/cat", $"{omz}/templates/zshrc.zsh-template"], ct).ConfigureAwait(false);
        if (template.ExitCode != 0 || template.TimedOut)
            return Failure(ZshrcLabel, template, ctx);

        var desired = ZshRc.Render(template.StdOut, shell.Theme, shell.Plugins);
        if (existing == desired)
            return new(ZshrcLabel, DevItemOutcome.AlreadyInOrder);

        var write = await wsl.WriteFileAsync(target.Distro, target.User, path, desired, ct).ConfigureAwait(false);
        if (write.ExitCode != 0 || write.TimedOut)
            return Failure(ZshrcLabel, write, ctx);
        return new(ZshrcLabel, DevItemOutcome.Done);
    }

    private static async Task<DevItem> EnsureLoginShellAsync(WslOptions target, string zshPath, StepContext ctx, CancellationToken ct)
    {
        var wsl = ctx.Services.Wsl;
        var entry = await wsl.ExecAsync(target.Distro, "root", ["/usr/bin/getent", "passwd", target.User], ct).ConfigureAwait(false);
        if (entry.ExitCode != 0 || entry.TimedOut)
            return Failure(LoginShellLabel, entry, ctx);

        var fields = entry.StdOut.Split('\n')[0].TrimEnd('\r').Split(':');
        if (fields.Length >= 7 && fields[6] == zshPath)
            return new(LoginShellLabel, DevItemOutcome.AlreadyInOrder);

        var chsh = await wsl.ExecAsync(target.Distro, "root", ["/usr/bin/chsh", "-s", zshPath, target.User], ct).ConfigureAwait(false);
        if (chsh.ExitCode != 0 || chsh.TimedOut)
            return Failure(LoginShellLabel, chsh, ctx);
        return new(LoginShellLabel, DevItemOutcome.Done);
    }

    private static bool IsFatal(ProcessResult result) => WslClient.IsVirtualizationError(result) || WslClient.IsNetworkError(result);

    private static DevItem Failure(string label, ProcessResult result, StepContext ctx)
    {
        ctx.Log.Warning("wsl.exe failed for {Item} with exit code {Exit}: {Err}", label, result.ExitCode, result.StdErr.Trim());
        var next =
            WslClient.IsVirtualizationError(result) ? "Next_EnableVirtualization"
            : WslClient.IsNetworkError(result) ? "Next_CheckNetwork"
            : "Next_SeeLog";
        return new(label, DevItemOutcome.Failed, Strings.Get(next));
    }

    private sealed class GitState
    {
        public bool Checked { get; set; }
    }

    private readonly record struct ZshResult(DevItem Item, string? Path, ProcessResult Last);

    private readonly record struct CloneResult(DevItem Item, bool Fatal);

    // ---- Windows side ----

    private async Task RunWindowsAsync(WindowsShellOptions windows, List<DevItem> items, StepContext ctx, CancellationToken ct)
    {
        var files = ctx.Services.Files;
        var profiles = ProfileTargets(files, windows.Theme);
        var fragmentPath = env.Expand(@"%LOCALAPPDATA%\Microsoft\Windows Terminal\Fragments\FormatKit\formatkit.json");
        var fontLabel = $"font {windows.Font}";

        var ohMyPosh = ctx.Services.Tools.Find("oh-my-posh.exe", KnownTools.OhMyPosh);
        if (ohMyPosh is null)
        {
            var missing = Strings.Get("Next_AddOhMyPosh");
            items.Add(new(fontLabel, DevItemOutcome.Failed, missing));
            items.AddRange(profiles.Select(p => new DevItem(p.Path, DevItemOutcome.Failed, missing)));
            items.Add(new(fragmentPath, DevItemOutcome.Failed, missing));
            return;
        }

        items.Add(IsFontInstalled(windows.Font, ctx)
            ? new DevItem(fontLabel, DevItemOutcome.AlreadyInOrder)
            : await InstallFontAsync(ohMyPosh, windows.Font, fontLabel, ctx, ct).ConfigureAwait(false));
        foreach (var profile in profiles)
        {
            var item = EnsureProfile(profile.Path, profile.InitLine, ctx);
            items.Add(profile.WindowsPowerShell ? RequireScripts(item, ctx) : item);
        }
        items.Add(EnsureFragment(fragmentPath, windows.Font, ctx));
    }

    /// <summary>PowerShell 7 only when it is installed; Windows PowerShell 5.1 always (its init command is <c>powershell</c>, spec 12).</summary>
    private List<(string Path, string InitLine, bool WindowsPowerShell)> ProfileTargets(IFileSystem files, string theme)
    {
        const string ProfileFile = "Microsoft.PowerShell_profile.ps1";
        List<(string, string, bool)> targets = [];
        if (files.FileExists(env.Expand(@"%ProgramFiles%\PowerShell\7\pwsh.exe")))
            targets.Add((Path.Combine(env.Documents, "PowerShell", ProfileFile), Init("pwsh", theme), false));
        targets.Add((Path.Combine(env.Documents, "WindowsPowerShell", ProfileFile), Init("powershell", theme), true));
        return targets;
    }

    private static string Init(string shell, string theme) => $"oh-my-posh init {shell} --config {theme} | Invoke-Expression";

    /// <summary>The profile is written regardless, but Windows PowerShell 5.1 only loads it when its execution policy allows scripts (spec 12).</summary>
    private static DevItem RequireScripts(DevItem item, StepContext ctx)
    {
        if (item.Outcome is not (DevItemOutcome.Done or DevItemOutcome.AlreadyInOrder))
            return item;

        var policy = PowerShellExecutionPolicy.Effective(ctx.Services.Registry);
        if (PowerShellExecutionPolicy.RunsScripts(policy))
            return item;

        ctx.Log.Warning("Windows PowerShell execution policy is {Policy}; {Path} will not load", policy, item.Label);
        return item with { Outcome = DevItemOutcome.Pending, NextAction = Strings.Get("Next_EnablePsScripts"), Detail = policy };
    }

    /// <summary>
    /// Installing again replaces the per-user font files, which crashed a Windows Terminal opened right after (spec 12), so an installed
    /// family is left alone. Font names look like "MesloLGM Nerd Font Regular (TrueType)"; the space after the family keeps "MesloLGMX" out.
    /// </summary>
    private static bool IsFontInstalled(string font, StepContext ctx)
    {
        var prefix = TerminalFragment.FontFamilies[font] + " ";
        return new[] { @"HKCU\Software\Microsoft\Windows NT\CurrentVersion\Fonts", @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts" }
            .Any(key => ctx.Services.Registry.GetValueNames(key).Any(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Exit 0 also when the font is already installed (spec 12), so success is reported as done.</summary>
    private static async Task<DevItem> InstallFontAsync(string ohMyPosh, string font, string label, StepContext ctx, CancellationToken ct)
    {
        try
        {
            var spec = new ProcessSpec(ohMyPosh, ["font", "install", font.ToLowerInvariant()], Timeouts.Install);
            var run = await ctx.Services.UserContext.RunAsync(spec, ct).ConfigureAwait(false);
            if (run is { ExitCode: 0, TimedOut: false })
                return new(label, DevItemOutcome.Done);
            ctx.Log.Warning("oh-my-posh font install failed with exit code {Exit}", run.ExitCode);
        }
        catch (FileNotFoundException e)
        {
            ctx.Log.Warning(e, "oh-my-posh could not be started");
        }

        return new(label, DevItemOutcome.Failed, Strings.Get("Next_SeeLog"));
    }

    private static DevItem EnsureProfile(string path, string initLine, StepContext ctx)
    {
        var files = ctx.Services.Files;
        try
        {
            var existing = files.FileExists(path) ? files.ReadAllBytes(path) : [];
            var text = Utf8.GetString(existing).TrimStart('\uFEFF');
            var appended = PowerShellProfileBlock.Append(text, initLine);
            if (appended is null)
                return new(path, DevItemOutcome.AlreadyInOrder);

            // Only the new block's bytes are added, so a profile in another encoding keeps what it has.
            var suffix = appended[text.Length..];
            files.CreateDirectory(Path.GetDirectoryName(path)!);
            files.WriteAllBytes(path, [.. existing, .. Utf8.GetBytes(suffix)]);
            return new(path, DevItemOutcome.Done);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ctx.Log.Warning(e, "Could not update {Path}", path);
            return new(path, DevItemOutcome.Failed, Strings.Get("Next_SeeLog"));
        }
    }

    private static DevItem EnsureFragment(string path, string font, StepContext ctx)
    {
        var files = ctx.Services.Files;
        var desired = TerminalFragment.Render(font);
        try
        {
            if (files.FileExists(path) && files.ReadAllText(path).Replace("\r\n", "\n") == desired)
                return new(path, DevItemOutcome.AlreadyInOrder);

            files.CreateDirectory(Path.GetDirectoryName(path)!);
            files.WriteAllTextAtomic(path, desired);
            return new(path, DevItemOutcome.Done);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ctx.Log.Warning(e, "Could not write {Path}", path);
            return new(path, DevItemOutcome.Failed, Strings.Get("Next_SeeLog"));
        }
    }
}
