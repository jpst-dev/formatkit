using FormatKit.Core.SystemAccess;

namespace FormatKit.Prepare;

/// <summary>Knows which paths a Windows reinstall destroys: the system drive and volumes backed by a VHDX on it. UNC paths are never lost.</summary>
public sealed class WindowsVolume(char systemDrive, IReadOnlyList<VolumeInfo> volumes)
{
    private readonly char _system = char.ToUpperInvariant(systemDrive);

    public IReadOnlyList<VolumeInfo> LostVhdxVolumes { get; } =
        [.. volumes.Where(v => DriveOf(v.BackingVhdx) == char.ToUpperInvariant(systemDrive)
                               && char.ToUpperInvariant(v.Letter) != char.ToUpperInvariant(systemDrive))];

    public bool IsLost(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return false;
        var letter = DriveOf(Path.GetFullPath(path));
        if (letter is null)
            return false;
        return letter == _system || LostVhdxVolumes.Any(v => char.ToUpperInvariant(v.Letter) == letter);
    }

    private static char? DriveOf(string? path) =>
        path is { Length: >= 2 } && path[1] == ':' && char.IsLetter(path[0]) ? char.ToUpperInvariant(path[0]) : null;
}
