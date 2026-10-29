using FormatKit.Core;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Prepare.Git;

/// <summary>A git call failed, timed out or was refused. The message never contains git's output.</summary>
public sealed class GitInspectionException(string message, bool isDubiousOwnership = false) : Exception(message)
{
    public bool IsDubiousOwnership { get; } = isDubiousOwnership;
}

/// <summary>Finds repositories and reads their state with read-only git queries. Never asks for remote URLs.</summary>
public sealed class GitRepoInspector(IProcessRunner processes, IFileSystem files, string gitExe)
{
    private static readonly string[] SkippedFolders = ["AppData", "node_modules"];

    private static readonly IReadOnlyDictionary<string, string> GitEnvironment = new Dictionary<string, string>
    {
        ["LC_ALL"] = "C",
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GIT_OPTIONAL_LOCKS"] = "0",
    };

    /// <summary>Folders under <paramref name="root"/> (depth 0) holding a <c>.git</c> folder or file; does not look inside a repository it found.</summary>
    public IReadOnlyList<string> Find(string root, int maxDepth, Action<string> onUnreadable)
    {
        var found = new List<string>();
        Walk(root, 0);
        return found;

        void Walk(string dir, int depth)
        {
            var git = Path.Combine(dir, ".git");
            if (files.DirectoryExists(git) || files.FileExists(git))
            {
                found.Add(dir);
                return;
            }

            if (depth >= maxDepth)
                return;
            foreach (var child in files.EnumerateDirectories(dir, onUnreadable))
            {
                if (SkippedFolders.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
                    continue;
                Walk(child, depth + 1);
            }
        }
    }

    public async Task<RepoState> InspectAsync(string repo, CancellationToken ct)
    {
        var status = await GitAsync(repo, ["status", "--porcelain=v2", "--branch"], ct);
        var refs = await GitAsync(repo, ["for-each-ref", "--format=%(refname:short)%09%(upstream:short)%09%(upstream:track,nobracket)", "refs/heads"], ct);
        var stash = await GitAsync(repo, ["stash", "list"], ct);
        var remote = await GitAsync(repo, ["remote"], ct);

        var uncommitted = Lines(status).Count(l => l.StartsWith("1 ") || l.StartsWith("2 ") || l.StartsWith("u ") || l.StartsWith("? "));
        var ahead = new List<BranchAhead>();
        var noUpstream = new List<string>();
        foreach (var line in Lines(refs))
        {
            var parts = line.Split('\t');
            var name = parts[0];
            var upstream = parts.Length > 1 ? parts[1] : "";
            var track = parts.Length > 2 ? parts[2] : "";
            if (upstream.Length == 0 || track == "gone")
                noUpstream.Add(name);
            else if (AheadCount(track) is > 0 and var commits)
                ahead.Add(new BranchAhead(name, commits));
        }

        return new RepoState(repo, uncommitted, ahead, noUpstream, Lines(stash).Count(), Lines(remote).Any());
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0);

    /// <summary>Reads "ahead 2" or "ahead 2, behind 3"; empty or "behind 3" means 0.</summary>
    private static int AheadCount(string track)
    {
        foreach (var part in track.Split(','))
        {
            var words = part.Trim().Split(' ');
            if (words.Length == 2 && words[0] == "ahead" && int.TryParse(words[1], out var n))
                return n;
        }

        return 0;
    }

    private async Task<string> GitAsync(string repo, string[] args, CancellationToken ct)
    {
        var spec = new ProcessSpec(gitExe, ["-C", repo, .. args], Timeouts.Query, GitEnvironment);
        var result = await processes.RunAsync(spec, ct);
        if (result.TimedOut)
            throw new GitInspectionException($"git {args[0]} timed out");
        if (result.ExitCode == 128 && result.StdErr.Contains("detected dubious ownership", StringComparison.Ordinal))
            throw new GitInspectionException("git refused the repository (dubious ownership)", isDubiousOwnership: true);
        if (result.ExitCode != 0)
            throw new GitInspectionException($"git {args[0]} exited with code {result.ExitCode}");
        return result.StdOut;
    }
}
