using FormatKit.Core;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Dev.Localization;

namespace FormatKit.Dev.Steps;

/// <summary>Installs WSL and the distro, creates the user and writes the WSL config files (spec 6.1).</summary>
internal sealed class WslStep(WslOptions options, DevEnvironment env, TimeProvider time) : IStep
{
    public string Id => "dev.wsl";

    public string Title => Strings.Get("Step_Title_Wsl");

    public bool RequiresAdmin => true;

    public bool IsCritical => false;

    public Task<bool> CheckAsync(StepContext ctx, CancellationToken ct) => Task.FromResult(false);

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var wsl = ctx.Services.Wsl;

        if (!ctx.Services.UserSession.IsConsoleUser)
        {
            var wrong = Strings.Get("Next_WrongAccount");
            List<DevItem> all =
            [
                new("WSL", DevItemOutcome.Failed, wrong),
                new(options.Distro, DevItemOutcome.Failed, wrong),
                new(UserLabel(options), DevItemOutcome.Failed, wrong),
            ];
            if (options.Config is not null)
                all.Add(new(".wslconfig", DevItemOutcome.Failed, wrong));
            return DevStepResult.From(all);
        }

        var items = new List<DevItem>();

        if (!await wsl.IsAvailableAsync(ct).ConfigureAwait(false))
        {
            var install = await wsl.InstallPlatformAsync(ct).ConfigureAwait(false);
            if (install.ExitCode != 0 || install.TimedOut)
            {
                items.Add(Failure("WSL", install, ctx));
                return DevStepResult.From(items);
            }

            // The platform only works after a reboot, although `wsl --status` already exits 0 (spec 12).
            items.Add(new("WSL", DevItemOutcome.Done));
            return DevStepResult.From(items, reboot: true);
        }

        items.Add(new("WSL", DevItemOutcome.AlreadyInOrder));

        var distros = await wsl.ListDistrosAsync(ct).ConfigureAwait(false);
        if (distros.Contains(options.Distro, StringComparer.OrdinalIgnoreCase))
        {
            items.Add(new(options.Distro, DevItemOutcome.AlreadyInOrder));
        }
        else
        {
            var install = await wsl.InstallDistroAsync(options.Distro, ct).ConfigureAwait(false);
            if (install.ExitCode != 0 || install.TimedOut)
            {
                items.Add(Failure(options.Distro, install, ctx));
                return DevStepResult.From(items);
            }

            items.Add(new(options.Distro, DevItemOutcome.Done));
        }

        var user = await RunUserAsync(options, ctx, ct).ConfigureAwait(false);
        items.Add(user.Item);
        if (user.Stop)
            return DevStepResult.From(items);

        if (options.Config is not null)
            items.Add(EnsureWslConfig(options.Config, ctx));

        return DevStepResult.From(items);
    }

    /// <summary>Creates the Linux user if missing, has its password set when someone can type, and makes it the default user.</summary>
    internal static async Task<DevItem> EnsureUserAsync(WslOptions options, StepContext ctx, CancellationToken ct) =>
        (await RunUserAsync(options, ctx, ct).ConfigureAwait(false)).Item;

    private static async Task<UserResult> RunUserAsync(WslOptions options, StepContext ctx, CancellationToken ct)
    {
        var wsl = ctx.Services.Wsl;
        var label = UserLabel(options);
        var wrong = ctx.Services.UserSession.IsConsoleUser ? null : new DevItem(label, DevItemOutcome.Failed, Strings.Get("Next_WrongAccount"));
        if (wrong is not null)
            return wrong;

        var changed = false;

        var id = await wsl.ExecAsync(options.Distro, "root", ["/usr/bin/id", "-u", options.User], ct).ConfigureAwait(false);
        if (id.ExitCode != 0 || id.TimedOut)
        {
            if (WslClient.IsVirtualizationError(id) || WslClient.IsNetworkError(id))
                return Fail(label, id, ctx);

            var add = await wsl.ExecAsync(
                options.Distro, "root",
                ["/usr/sbin/useradd", "-m", "-G", "sudo", "-s", "/bin/bash", options.User], ct).ConfigureAwait(false);
            if (add.ExitCode != 0 || add.TimedOut)
                return Fail(label, add, ctx);
            changed = true;
        }

        var status = await wsl.ExecAsync(options.Distro, "root", ["/usr/bin/passwd", "-S", options.User], ct).ConfigureAwait(false);
        if (status.ExitCode != 0 || status.TimedOut)
            return Fail(label, status, ctx);

        var passwordPending = false;
        if (IsLocked(status.StdOut))
        {
            if (ctx.Services.Console.CanPrompt)
            {
                var exit = await ctx.Services.Console.RunAttachedAsync(
                    WslClient.ExecSpec(options.Distro, "root", ["/usr/bin/passwd", options.User], Timeouts.Install), ct).ConfigureAwait(false);
                if (exit != 0)
                    return new DevItem(label, DevItemOutcome.Failed, Strings.Get("Next_SeeLog"));
                changed = true;
            }
            else
            {
                passwordPending = true;
            }
        }

        // Only a plain Linux exit 1 means the file is missing; a timeout or a wsl.exe failure must not look like an empty file.
        var cat = await wsl.ExecAsync(options.Distro, "root", ["/usr/bin/cat", "/etc/wsl.conf"], ct).ConfigureAwait(false);
        if (cat.TimedOut || cat.ExitCode < 0 || cat.ExitCode > 1)
            return Fail(label, cat, ctx);

        var existing = cat.ExitCode == 0 ? cat.StdOut : "";
        var merged = WslConf.SetDefaultUser(existing, options.User);
        if (merged != existing)
        {
            var write = await wsl.WriteFileAsync(options.Distro, "root", "/etc/wsl.conf", merged, ct).ConfigureAwait(false);
            if (write.ExitCode != 0 || write.TimedOut)
                return Fail(label, write, ctx);
            await wsl.TerminateAsync(options.Distro, ct).ConfigureAwait(false);
            changed = true;
        }

        if (passwordPending)
            return new DevItem(label, DevItemOutcome.Pending, Strings.Get("Next_RunWslUser"));

        return new DevItem(label, changed ? DevItemOutcome.Done : DevItemOutcome.AlreadyInOrder);
    }

    private DevItem EnsureWslConfig(WslConfig config, StepContext ctx)
    {
        var files = ctx.Services.Files;
        var path = Path.Combine(env.UserProfile, ".wslconfig");
        var desired = WslConf.WslConfig(config);

        try
        {
            if (files.FileExists(path))
            {
                if (files.ReadAllText(path).Replace("\r\n", "\n") == desired)
                    return new(".wslconfig", DevItemOutcome.AlreadyInOrder);
                files.Move(path, Backup.PathFor(path, time.GetLocalNow()));
            }

            files.WriteAllTextAtomic(path, desired);
            return new(".wslconfig", DevItemOutcome.Done);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ctx.Log.Warning(e, "Could not write {Path}", path);
            return new(".wslconfig", DevItemOutcome.Failed, Strings.Get("Next_SeeLog"));
        }
    }

    private static string UserLabel(WslOptions options) => $"user {options.User}";

    /// <summary><c>passwd -S</c> prints <c>name L date ...</c>; the second field is L when locked, P when a password is set.</summary>
    private static bool IsLocked(string output)
    {
        var fields = output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 1 && fields[1] == "L";
    }

    private static UserResult Fail(string label, ProcessResult result, StepContext ctx) =>
        new(Failure(label, result, ctx), WslClient.IsVirtualizationError(result) || WslClient.IsNetworkError(result));

    private static DevItem Failure(string label, ProcessResult result, StepContext ctx)
    {
        ctx.Log.Warning("wsl.exe failed for {Item} with exit code {Exit}: {Err}", label, result.ExitCode, result.StdErr.Trim());
        var next =
            WslClient.IsVirtualizationError(result) ? "Next_EnableVirtualization"
            : WslClient.IsNetworkError(result) ? "Next_CheckNetwork"
            : "Next_SeeLog";
        return new(label, DevItemOutcome.Failed, Strings.Get(next));
    }

    /// <summary>The user item, and whether later items must not run (virtualization or network failure).</summary>
    private readonly record struct UserResult(DevItem Item, bool Stop)
    {
        public static implicit operator UserResult(DevItem item) => new(item, false);
    }
}
