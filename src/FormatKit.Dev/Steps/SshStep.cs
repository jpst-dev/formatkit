using FormatKit.Core;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Dev.Localization;

namespace FormatKit.Dev.Steps;

/// <summary>Creates the SSH key, starts the ssh-agent, loads the key into it and points Git at the Windows ssh (spec 6.3).</summary>
internal sealed class SshStep(SshOptions options, DevEnvironment env) : IStep
{
    private const string GitHubKeysUrl = "https://github.com/settings/ssh/new";
    private const string AgentService = "ssh-agent";
    private const int ServiceAlreadyRunning = 1056;

    public string Id => "dev.ssh";

    public string Title => Strings.Get("Step_Title_Ssh");

    public bool RequiresAdmin => true;

    public bool IsCritical => false;

    public Task<bool> CheckAsync(StepContext ctx, CancellationToken ct) => Task.FromResult(false);

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct) =>
        DevStepResult.From(await RunAsync(options, env, ctx, ct).ConfigureAwait(false));

    /// <summary>
    /// Also behind <c>formatkit dev ssh-key</c>. Whatever can ask for a passphrase (key creation, ssh-add) runs on the
    /// attached console and nothing it prints is read or logged; only public data is captured.
    /// </summary>
    internal static async Task<IReadOnlyList<DevItem>> RunAsync(SshOptions options, DevEnvironment env, StepContext ctx, CancellationToken ct)
    {
        var keyFile = Path.Combine(env.UserProfile, ".ssh", $"id_{options.KeyType}");
        var pubFile = keyFile + ".pub";

        var keygen = ctx.Services.Tools.Find("ssh-keygen.exe", KnownTools.SshKeygen);
        var keyItem = await EnsureKeyAsync(options, keyFile, keygen, ctx, ct).ConfigureAwait(false);
        var agentItem = await EnsureAgentAsync(ctx, ct).ConfigureAwait(false);

        DevItem agentKeyItem;
        if (agentItem.Outcome == DevItemOutcome.Failed || !ctx.Services.Files.FileExists(pubFile))
            agentKeyItem = new DevItem("agent key", DevItemOutcome.NotApplicable);
        else
            agentKeyItem = await EnsureAgentKeyAsync(keyFile, pubFile, keygen, ctx, ct).ConfigureAwait(false);

        const string gitLabel = "Git uses Windows SSH";
        var git = ctx.Services.Tools.Find("git.exe", KnownTools.Git);
        var gitItem = git is null
            ? new DevItem(gitLabel, DevItemOutcome.NotApplicable)
            : (await GitStep.EnsureConfigAsync(git, "core.sshCommand", KnownTools.WindowsSsh, ctx, ct).ConfigureAwait(false)) with
            {
                Label = gitLabel,
            };

        if (TryReadPublicKey(pubFile, ctx, out var publicKey))
        {
            ctx.Services.Console.WriteLine(publicKey);
            ctx.Services.Console.WriteLine(GitHubKeysUrl);
            keyItem = keyItem with { Detail = $"{publicKey} {GitHubKeysUrl}" };
        }

        return [keyItem, agentItem, agentKeyItem, gitItem];
    }

    private static async Task<DevItem> EnsureKeyAsync(SshOptions options, string keyFile, string? keygen, StepContext ctx, CancellationToken ct)
    {
        if (ctx.Services.Files.FileExists(keyFile))
            return new DevItem("key", DevItemOutcome.AlreadyInOrder);

        if (!ctx.Services.Console.CanPrompt)
            return new DevItem("key", DevItemOutcome.Pending, Strings.Get("Next_RunSshKey"));

        if (keygen is null)
            return new DevItem("key", DevItemOutcome.Failed, Strings.Get("Next_SeeLog"));

        List<string> args = ["-t", options.KeyType];
        if (options.KeyType == "rsa")
            args.AddRange(["-b", "4096"]);
        args.AddRange(["-C", options.Comment, "-f", keyFile]);

        var exit = await ctx.Services.Console.RunAttachedAsync(new ProcessSpec(keygen, args, Timeouts.Install), ct).ConfigureAwait(false);
        if (exit == 0)
            return new DevItem("key", DevItemOutcome.Done);

        ctx.Log.Warning("ssh-keygen exited with code {Exit}", exit);
        return new DevItem("key", DevItemOutcome.Failed, Strings.Get("Next_SeeLog"));
    }

    private static async Task<DevItem> EnsureAgentAsync(StepContext ctx, CancellationToken ct)
    {
        var failed = new DevItem("ssh-agent", DevItemOutcome.Failed, Strings.Get("Next_SeeLog"));
        var services = ctx.Services.ServiceControl;

        var startType = services.GetStartType(AgentService);
        if (startType is null)
        {
            ctx.Log.Warning("The {Service} service does not exist", AgentService);
            return failed;
        }

        var changed = false;
        if (startType != ServiceStartType.Automatic)
        {
            await services.SetStartTypeAsync(AgentService, ServiceStartType.Automatic, ct).ConfigureAwait(false);
            changed = true;
        }

        var start = await GitStep.RunAsync(ctx, "sc.exe", ["start", AgentService], ct).ConfigureAwait(false);
        if (start is null || start.TimedOut || start.ExitCode is not (0 or ServiceAlreadyRunning))
        {
            ctx.Log.Warning("sc.exe start {Service} failed with exit code {Exit}", AgentService, start?.ExitCode);
            return failed;
        }

        return new DevItem("ssh-agent", changed || start.ExitCode == 0 ? DevItemOutcome.Done : DevItemOutcome.AlreadyInOrder);
    }

    private static async Task<DevItem> EnsureAgentKeyAsync(string keyFile, string pubFile, string? keygen, StepContext ctx, CancellationToken ct)
    {
        var failed = new DevItem("agent key", DevItemOutcome.Failed, Strings.Get("Next_SeeLog"));
        var sshAdd = ctx.Services.Tools.Find("ssh-add.exe", KnownTools.SshAdd);
        if (keygen is null || sshAdd is null)
            return failed;

        // Listing public fingerprints needs no passphrase, so these two can be captured.
        var fingerprintResult = await GitStep.RunAsync(ctx, keygen, ["-lf", pubFile], ct).ConfigureAwait(false);
        var fingerprint = fingerprintResult is { ExitCode: 0, TimedOut: false } ? SecondField(fingerprintResult.StdOut) : null;
        if (fingerprint is null)
            return failed;

        var loaded = await GitStep.RunAsync(ctx, sshAdd, ["-l"], ct).ConfigureAwait(false);
        if (loaded is { ExitCode: 0, TimedOut: false }
            && loaded.StdOut.Split('\n').Any(line => SecondField(line) == fingerprint))
            return new DevItem("agent key", DevItemOutcome.AlreadyInOrder);

        if (!ctx.Services.Console.CanPrompt)
            return new DevItem("agent key", DevItemOutcome.Pending, Strings.Get("Next_RunSshKey"));

        var exit = await ctx.Services.Console.RunAttachedAsync(new ProcessSpec(sshAdd, [keyFile], Timeouts.Install), ct).ConfigureAwait(false);
        if (exit == 0)
            return new DevItem("agent key", DevItemOutcome.Done);

        ctx.Log.Warning("ssh-add exited with code {Exit}", exit);
        return failed;
    }

    private static string? SecondField(string line)
    {
        var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 1 ? fields[1] : null;
    }

    /// <summary>Only the <c>.pub</c> file is read; the private key is never opened.</summary>
    private static bool TryReadPublicKey(string pubFile, StepContext ctx, out string publicKey)
    {
        publicKey = "";
        try
        {
            if (!ctx.Services.Files.FileExists(pubFile))
                return false;
            publicKey = ctx.Services.Files.ReadAllText(pubFile).Trim();
            return publicKey.Length > 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ctx.Log.Warning(e, "Could not read {Path}", pubFile);
            return false;
        }
    }
}
