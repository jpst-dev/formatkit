using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeWindowsImages(InMemoryFileSystem? files = null) : IWindowsImages
{
    /// <summary>The images <see cref="ListAsync"/> returns, by image path.</summary>
    public Dictionary<string, IReadOnlyList<WindowsImageInfo>> Images { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Wim, string Swm, int SizeMb)> Splits { get; } = [];

    public Exception? ThrowOnSplit { get; set; }

    public Task<IReadOnlyList<WindowsImageInfo>> ListAsync(string imagePath, CancellationToken ct) =>
        Images.TryGetValue(imagePath, out var list)
            ? Task.FromResult(list)
            : Task.FromException<IReadOnlyList<WindowsImageInfo>>(new InvalidOperationException($"No image at '{imagePath}'."));

    /// <summary>Records the split and, with a file system, writes <c>install.swm</c> and <c>install2.swm</c> next to the target name.</summary>
    public Task SplitAsync(string wimPath, string swmPath, int partSizeMb, CancellationToken ct)
    {
        if (ThrowOnSplit is not null)
        {
            return Task.FromException(ThrowOnSplit);
        }

        Splits.Add((wimPath, swmPath, partSizeMb));
        if (files is not null)
        {
            var dir = Path.GetDirectoryName(swmPath) ?? "";
            var name = Path.GetFileNameWithoutExtension(swmPath);
            var ext = Path.GetExtension(swmPath);
            files.Seed(swmPath, "");
            files.Seed(Path.Combine(dir, name + "2" + ext), "");
        }

        return Task.CompletedTask;
    }
}
