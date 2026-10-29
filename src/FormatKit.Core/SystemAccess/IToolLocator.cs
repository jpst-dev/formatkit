namespace FormatKit.Core.SystemAccess;

public interface IToolLocator
{
    /// <summary>Returns the full path of <paramref name="exeName"/>, or null when it is nowhere.</summary>
    string? Find(string exeName, IReadOnlyList<string> knownPaths);
}
