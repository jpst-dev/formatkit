namespace FormatKit.Core.SystemAccess;

/// <summary>
/// Looks in the machine and user <c>Path</c> as stored now (not the possibly stale process copy), then in known install paths.
/// </summary>
/// <param name="machinePath">Reads an environment variable at machine scope.</param>
/// <param name="userPath">Reads an environment variable at user scope.</param>
public sealed class ToolLocator(IFileSystem files, Func<string, string?> machinePath, Func<string, string?> userPath) : IToolLocator
{
    public string? Find(string exeName, IReadOnlyList<string> knownPaths)
    {
        foreach (var scope in new[] { machinePath("Path"), userPath("Path") })
        {
            foreach (var raw in (scope ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var candidate = Path.Combine(Environment.ExpandEnvironmentVariables(raw).Trim('"'), exeName);
                if (files.FileExists(candidate))
                {
                    return candidate;
                }
            }
        }

        return knownPaths
            .Select(Environment.ExpandEnvironmentVariables)
            .FirstOrDefault(files.FileExists);
    }
}
