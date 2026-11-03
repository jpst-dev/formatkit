using FormatKit.Media.Setup;

namespace FormatKit.Media.Building;

/// <summary>A mounted and validated ISO. Disposing dismounts it (best effort).</summary>
public sealed record PreparedMedia(string MountRoot, InstallImage Image, ResolvedSetup Setup, long RequiredBytes) : IAsyncDisposable
{
    private int _disposed;

    internal Func<Task>? Dismount { get; init; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && Dismount is not null)
            await Dismount();
    }
}
