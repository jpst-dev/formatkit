using FormatKit.Prepare.Git;
using FormatKit.Prepare.Localization;

namespace FormatKit.Prepare.Checks.Developer;

/// <summary>Finds Git repositories a reinstall would erase and reports the work that exists only on this PC. Never reads a remote URL.</summary>
public sealed class GitReposCheck : IPrepareCheck
{
    public const string CheckId = "dev.git-repos";

    public const int MaxDepth = 5;

    public string Id => CheckId;

    public PrepareGroup Group => PrepareGroup.Developer;

    public async Task<IReadOnlyList<PrepareItem>> RunAsync(PrepareContext context, CancellationToken ct)
    {
        var scanner = new GitRepoInspector(context.Services.Processes, context.Services.Files, "git.exe");
        var unreadable = 0;
        var roots = new List<string> { context.Env.UserProfile };
        roots.AddRange(context.Windows.LostVhdxVolumes.Select(v => $@"{char.ToUpperInvariant(v.Letter)}:\"));

        var repos = roots
            .SelectMany(root => scanner.Find(root, MaxDepth, _ => unreadable++))
            .Where(context.Windows.IsLost)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unreadable > 0)
            context.Log.Debug("{Count} folders could not be listed while looking for repositories", unreadable);
        if (repos.Count == 0)
            return unreadable == 0 ? [] : [BlindSpot(unreadable)];

        var items = await InspectAllAsync(context, repos, ct);
        return unreadable == 0 ? items : [.. items.Select(i => i with { Detail = $"{i.Detail} {Unreadable(unreadable)}".Trim() })];
    }

    private async Task<List<PrepareItem>> InspectAllAsync(PrepareContext context, List<string> repos, CancellationToken ct)
    {

        var git = context.Services.Tools.Find("git.exe", [context.Env.Expand(@"%ProgramFiles%\Git\cmd\git.exe")]);
        if (git is null)
            return [GitMissing(repos)];

        var inspector = new GitRepoInspector(context.Services.Processes, context.Services.Files, git);
        var items = new List<PrepareItem>();
        var clean = 0;
        foreach (var (repo, index) in repos.Select((r, i) => (r, i)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var state = await inspector.InspectAsync(repo, ct);
                if (state.IsSafe)
                    clean++;
                else
                    items.Add(Unsafe(state));
            }
            catch (FileNotFoundException)
            {
                items.Add(GitMissing(repos.Skip(index).ToList()));
                break;
            }
            catch (GitInspectionException ex)
            {
                context.Log.Warning("Could not inspect {Repo}: {Reason}", repo, ex.Message);
                items.Add(ex.IsDubiousOwnership ? DubiousOwnership(repo) : Failed(repo));
            }
        }

        if (clean > 0)
        {
            var key = clean == 1 ? "GitRepos_Ok_Title_One" : "GitRepos_Ok_Title_Many";
            items.Add(new PrepareItem(Id, Group, PrepareStatus.Ok, Strings.Format(key, clean), Strings.Get("GitRepos_Ok_Detail")));
        }

        return items;
    }

    private PrepareItem Unsafe(RepoState state)
    {
        var parts = new List<string>();
        if (state.Uncommitted > 0)
            parts.Add(Count("GitRepos_Uncommitted", state.Uncommitted));
        foreach (var branch in state.Ahead)
            parts.Add(branch.Commits == 1
                ? Strings.Format("GitRepos_Ahead_One", branch.Name)
                : Strings.Format("GitRepos_Ahead_Many", branch.Name, branch.Commits));
        if (state.NoUpstream.Count > 0)
            parts.Add(state.NoUpstream.Count == 1
                ? Strings.Format("GitRepos_NoUpstream_One", state.NoUpstream[0])
                : Strings.Format("GitRepos_NoUpstream_Many", string.Join(", ", state.NoUpstream)));
        if (state.DetachedUnpushed)
            parts.Add(Strings.Get("GitRepos_Detached"));
        if (state.Stashes > 0)
            parts.Add(Count("GitRepos_Stash", state.Stashes));
        if (!state.HasRemote)
            parts.Add(Strings.Get("GitRepos_NoRemote"));

        return new PrepareItem(Id, Group, PrepareStatus.Blocker,
            Strings.Format("GitRepos_Unsafe_Title", state.Path),
            string.Join(" ", parts),
            Strings.Format("GitRepos_Unsafe_Action", state.Path));
    }

    private PrepareItem BlindSpot(int unreadable) =>
        new(Id, Group, PrepareStatus.NotChecked,
            Strings.Get("GitRepos_Unreadable_Title"), Unreadable(unreadable), Strings.Get("GitRepos_Unreadable_Action"));

    private static string Unreadable(int count) =>
        count == 1 ? Strings.Get("GitRepos_Unreadable_One") : Strings.Format("GitRepos_Unreadable_Many", count);

    private static string Count(string prefix, int count) =>
        count == 1 ? Strings.Get(prefix + "_One") : Strings.Format(prefix + "_Many", count);

    private PrepareItem GitMissing(IReadOnlyList<string> repos) =>
        new(Id, Group, PrepareStatus.NotChecked,
            Strings.Get("GitRepos_NoGit_Title"),
            repos.Count == 1
                ? Strings.Format("GitRepos_NoGit_Detail_One", repos[0])
                : Strings.Format("GitRepos_NoGit_Detail_Many", string.Join("; ", repos)),
            Strings.Get("GitRepos_NoGit_Action"));

    private PrepareItem DubiousOwnership(string repo) =>
        new(Id, Group, PrepareStatus.NotChecked,
            Strings.Format("GitRepos_Failed_Title", repo),
            Strings.Get("GitRepos_Dubious_Detail"),
            Strings.Format("GitRepos_Dubious_Action", repo.Replace('\\', '/')));

    private PrepareItem Failed(string repo) =>
        new(Id, Group, PrepareStatus.NotChecked,
            Strings.Format("GitRepos_Failed_Title", repo),
            Strings.Get("GitRepos_Failed_Detail"),
            Strings.Format("GitRepos_Failed_Action", repo));
}
