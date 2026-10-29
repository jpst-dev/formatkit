namespace FormatKit.Prepare.Git;

/// <summary>A local branch with commits its upstream does not have.</summary>
public sealed record BranchAhead(string Name, int Commits);

/// <summary>What a reinstall would lose from one repository. Counts and branch names only, never a remote URL.</summary>
public sealed record RepoState(
    string Path,
    int Uncommitted,
    IReadOnlyList<BranchAhead> Ahead,
    IReadOnlyList<string> NoUpstream,
    int Stashes,
    bool HasRemote)
{
    public bool IsSafe => Uncommitted == 0 && Ahead.Count == 0 && NoUpstream.Count == 0 && Stashes == 0 && HasRemote;
}
