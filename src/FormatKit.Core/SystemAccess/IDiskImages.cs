namespace FormatKit.Core.SystemAccess;

/// <summary>Mounts and dismounts ISO images.</summary>
public interface IDiskImages
{
    /// <summary>Mounts the ISO and returns the root of its volume, such as <c>E:\</c>. Throws <see cref="InvalidOperationException"/> on failure.</summary>
    Task<string> MountAsync(string isoPath, CancellationToken ct);

    /// <summary>Dismounts the ISO. Throws <see cref="InvalidOperationException"/> on failure.</summary>
    Task DismountAsync(string isoPath, CancellationToken ct);
}
