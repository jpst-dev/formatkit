using FormatKit.Core.SystemAccess;

namespace FormatKit.Prepare;

/// <summary>Where a path ends up after a reinstall: kept, erased, or unknown because the volume list is not known.</summary>
public enum PathLocation { Kept, Lost, Unknown }

/// <summary>
/// Knows which paths a Windows reinstall destroys: the system drive and volumes backed by a VHDX on it. UNC paths are
/// never lost. The volume list is read once per run and shared here; when it could not be read, or does not include
/// the system drive, any other drive may be a virtual disk stored on the system drive, so its paths are
/// <see cref="PathLocation.Unknown"/>. A <c>subst</c> drive or a junction pointing into the system drive is not resolved.
/// </summary>
public sealed class WindowsVolume
{
    private readonly char _system;

    /// <param name="volumes">The fixed volumes, or null when listing them failed.</param>
    public WindowsVolume(char systemDrive, IReadOnlyList<VolumeInfo>? volumes)
    {
        _system = char.ToUpperInvariant(systemDrive);
        Volumes = volumes ?? [];
        VolumesKnown = volumes is not null && volumes.Any(v => char.ToUpperInvariant(v.Letter) == _system);
        LostVhdxVolumes = [.. Volumes.Where(v => DriveOf(v.BackingVhdx) == _system && char.ToUpperInvariant(v.Letter) != _system)];
    }

    /// <summary>The listed volumes; empty when listing failed.</summary>
    public IReadOnlyList<VolumeInfo> Volumes { get; }

    /// <summary>True when the volume list was read and includes the system drive.</summary>
    public bool VolumesKnown { get; }

    public IReadOnlyList<VolumeInfo> LostVhdxVolumes { get; }

    public PathLocation Locate(string path)
    {
        // \\?\C:\ and \\.\C:\ are local device paths; only \\server\share and \\?\UNC\ are network paths.
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            path = path[4..];
            if (path.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase))
                return PathLocation.Kept;
            if (DriveOf(path) is null)
                return PathLocation.Unknown;
        }
        else if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return PathLocation.Kept;
        }

        var letter = DriveOf(Path.GetFullPath(path));
        if (letter is null)
            return PathLocation.Unknown;
        if (letter == _system || LostVhdxVolumes.Any(v => char.ToUpperInvariant(v.Letter) == letter))
            return PathLocation.Lost;
        return VolumesKnown ? PathLocation.Kept : PathLocation.Unknown;
    }

    public bool IsLost(string path) => Locate(path) == PathLocation.Lost;

    /// <summary>Lost or unknown: a scan includes it, so nothing is skipped on the strength of an unknown volume list.</summary>
    public bool MayBeLost(string path) => Locate(path) != PathLocation.Kept;

    private static char? DriveOf(string? path) =>
        path is { Length: >= 2 } && path[1] == ':' && char.IsLetter(path[0]) ? char.ToUpperInvariant(path[0]) : null;
}
