namespace FormatKit.Core.SystemAccess;

/// <summary>One image (edition) inside an install.wim or install.esd. <c>Architecture</c>: 9 = amd64, 12 = arm64, 0 = x86.</summary>
public sealed record WindowsImageInfo(
    int Index,
    string Name,
    int Architecture,
    string Version,
    IReadOnlyList<string> Languages,
    int DefaultLanguageIndex);

/// <summary>Reads and splits Windows images. Both methods throw <see cref="InvalidOperationException"/> on failure.</summary>
public interface IWindowsImages
{
    Task<IReadOnlyList<WindowsImageInfo>> ListAsync(string imagePath, CancellationToken ct);

    /// <summary>Splits a WIM into .swm parts of at most <paramref name="partSizeMb"/> MB, the first named <paramref name="swmPath"/>.</summary>
    Task SplitAsync(string wimPath, string swmPath, int partSizeMb, CancellationToken ct);
}
