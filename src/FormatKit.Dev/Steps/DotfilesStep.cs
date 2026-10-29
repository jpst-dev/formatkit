using FormatKit.Core;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Dev.Localization;

namespace FormatKit.Dev.Steps;

/// <summary>Clones or updates the dotfiles repository (or uses a local folder) and copies the listed files into place (spec 6.4).</summary>
internal sealed class DotfilesStep(DotfilesOptions options, DevEnvironment env, TimeProvider time) : IStep
{
    private const string RepositoryLabel = "repository";

    public string Id => "dev.dotfiles";

    public string Title => Strings.Get("Step_Title_Dotfiles");

    public bool RequiresAdmin => false;

    public bool IsCritical => false;

    public Task<bool> CheckAsync(StepContext ctx, CancellationToken ct) => Task.FromResult(false);

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var items = new List<DevItem>();
        string root;
        if (options.Repo is null)
        {
            items.Add(new DevItem(RepositoryLabel, DevItemOutcome.NotApplicable));
            root = options.LocalPath!;
        }
        else
        {
            var repository = await EnsureRepositoryAsync(options.Repo, ctx, ct).ConfigureAwait(false);
            items.Add(repository);
            root = options.CloneTo;
            if (repository.Outcome == DevItemOutcome.Failed)
            {
                items.AddRange(options.Files.Select(f => new DevItem(f.To, DevItemOutcome.NotApplicable)));
                return DevStepResult.From(items);
            }
        }

        foreach (var file in options.Files)
            items.Add(EnsureFile(root, file, ctx));
        return DevStepResult.From(items);
    }

    private async Task<DevItem> EnsureRepositoryAsync(string repo, StepContext ctx, CancellationToken ct)
    {
        var git = ctx.Services.Tools.Find("git.exe", KnownTools.Git);
        if (git is null)
            return Failed(Strings.Get("Next_AddGit"));

        if (!ctx.Services.Files.DirectoryExists(options.CloneTo))
        {
            // "--" so neither value can be taken as an option.
            return await GitAsync(git, ["clone", "--", repo, options.CloneTo], ctx, ct).ConfigureAwait(false);
        }

        var origin = await GitStep.RunAsync(ctx, git, ["-C", options.CloneTo, "remote", "get-url", "origin"], ct).ConfigureAwait(false);
        if (origin is not { ExitCode: 0, TimedOut: false } || origin.StdOut.Trim() != repo)
        {
            ctx.Log.Warning("{Folder} is not a clone of {Repo}", options.CloneTo, repo);
            return Failed(Strings.Get("Next_OtherCloneTo"));
        }

        return await GitAsync(git, ["-C", options.CloneTo, "pull", "--ff-only"], ctx, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Git Credential Manager can only ask on the attached console, so with someone at the PC the command runs there;
    /// otherwise it runs captured and cannot wait for a prompt.
    /// </summary>
    private static async Task<DevItem> GitAsync(
        string git, string[] args, StepContext ctx, CancellationToken ct)
    {
        int? exit;
        try
        {
            if (ctx.Services.Console.CanPrompt)
            {
                exit = await ctx.Services.Console.RunAttachedAsync(new ProcessSpec(git, args, Timeouts.Install), ct).ConfigureAwait(false);
            }
            else
            {
                var environment = new Dictionary<string, string> { ["GIT_TERMINAL_PROMPT"] = "0" };
                var result = await ctx.Services.Processes.RunAsync(new ProcessSpec(git, args, Timeouts.Install, environment), ct).ConfigureAwait(false);
                exit = result.TimedOut ? null : result.ExitCode;
            }
        }
        catch (FileNotFoundException)
        {
            exit = null;
        }

        if (exit == 0)
            return new DevItem(RepositoryLabel, DevItemOutcome.Done);

        ctx.Log.Warning("git {Command} failed with exit code {Exit}", string.Join(' ', args.Take(3)), exit);
        return Failed(Strings.Get("Next_SeeLog"));
    }

    private DevItem EnsureFile(string root, DotfileEntry file, StepContext ctx)
    {
        var failed = new DevItem(file.To, DevItemOutcome.Failed, Strings.Get("Next_SeeLog"));
        var files = ctx.Services.Files;

        // Re-checked here: a "from" like ".. " or an absolute path passes a text check but not the resolved one.
        var source = Resolve(Path.Combine(root, file.From));
        var target = Resolve(file.To);
        if (source is null || !DevOptions.IsInside(source, root) || target is null || !DevOptions.IsInside(target, env.UserProfile))
        {
            ctx.Log.Warning("Dotfile {From} -> {To} resolves outside its allowed folder and was skipped", file.From, file.To);
            return failed;
        }

        try
        {
            if (!files.FileExists(source))
            {
                ctx.Log.Warning("Dotfile source {Source} does not exist", source);
                return failed with { Detail = source };
            }

            if (!files.FileExists(target))
            {
                files.CreateDirectory(Path.GetDirectoryName(target)!);
                files.Copy(source, target);
                return new DevItem(file.To, DevItemOutcome.Done);
            }

            if (files.ReadAllBytes(source).AsSpan().SequenceEqual(files.ReadAllBytes(target)))
                return new DevItem(file.To, DevItemOutcome.AlreadyInOrder);

            files.Move(target, Backup.PathFor(target, time.GetLocalNow()));
            files.Copy(source, target);
            return new DevItem(file.To, DevItemOutcome.Done);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ctx.Log.Warning(e, "Could not place {Target}", target);
            return failed;
        }
    }

    private static string? Resolve(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static DevItem Failed(string nextAction) => new(RepositoryLabel, DevItemOutcome.Failed, nextAction);
}
