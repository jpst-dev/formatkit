using FormatKit.Core;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Dev.Localization;

namespace FormatKit.Dev.Steps;

/// <summary>Sets the global Git settings the profile gives, touching only the ones that differ (spec 6.2).</summary>
internal sealed class GitStep(GitOptions options) : IStep
{
    public string Id => "dev.git";

    public string Title => Strings.Get("Step_Title_Git");

    public bool RequiresAdmin => false;

    public bool IsCritical => false;

    public Task<bool> CheckAsync(StepContext ctx, CancellationToken ct) => Task.FromResult(false);

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        List<(string Key, string Value)> settings = [];
        AddIfSet(settings, "user.name", options.Name);
        AddIfSet(settings, "user.email", options.Email);
        AddIfSet(settings, "init.defaultBranch", options.DefaultBranch);
        AddIfSet(settings, "core.editor", options.Editor);
        AddIfSet(settings, "core.autocrlf", options.AutoCrlf);

        var git = ctx.Services.Tools.Find("git.exe", KnownTools.Git);
        if (git is null)
        {
            var missing = Strings.Get("Next_AddGit");
            return DevStepResult.From(settings.Select(s => new DevItem(s.Key, DevItemOutcome.Failed, missing)).ToList());
        }

        var items = new List<DevItem>();
        foreach (var (key, value) in settings)
            items.Add(await EnsureConfigAsync(git, key, value, ctx, ct).ConfigureAwait(false));
        return DevStepResult.From(items);
    }

    /// <summary>Reads <c>git config --global --get</c> and sets the key only when the value differs.</summary>
    internal static async Task<DevItem> EnsureConfigAsync(string git, string key, string value, StepContext ctx, CancellationToken ct)
    {
        var current = await RunAsync(ctx, git, ["config", "--global", "--get", key], ct).ConfigureAwait(false);
        if (current is { ExitCode: 0, TimedOut: false } && current.StdOut.TrimEnd('\r', '\n') == value)
            return new DevItem(key, DevItemOutcome.AlreadyInOrder);

        var set = await RunAsync(ctx, git, ["config", "--global", key, value], ct).ConfigureAwait(false);
        if (set is { ExitCode: 0, TimedOut: false })
            return new DevItem(key, DevItemOutcome.Done);

        ctx.Log.Warning("git config failed for {Key} with exit code {Exit}: {Err}", key, set?.ExitCode, set?.StdErr.Trim());
        return new DevItem(key, DevItemOutcome.Failed, Strings.Get("Next_SeeLog"));
    }

    /// <summary>Runs a captured process; null when the executable vanished.</summary>
    internal static async Task<ProcessResult?> RunAsync(StepContext ctx, string file, string[] args, CancellationToken ct)
    {
        try
        {
            return await ctx.Services.Processes.RunAsync(new ProcessSpec(file, args, Timeouts.Query), ct).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static void AddIfSet(List<(string, string)> settings, string key, string? value)
    {
        if (value is not null)
            settings.Add((key, value));
    }
}
