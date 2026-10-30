using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeDiskImages : IDiskImages
{
    /// <summary>The root <see cref="MountAsync"/> returns.</summary>
    public string Root { get; set; } = @"E:\";

    public List<string> Mounted { get; } = [];

    public List<string> Dismounted { get; } = [];

    public Exception? ThrowOnMount { get; set; }

    public Exception? ThrowOnDismount { get; set; }

    public Task<string> MountAsync(string isoPath, CancellationToken ct)
    {
        if (ThrowOnMount is not null)
        {
            return Task.FromException<string>(ThrowOnMount);
        }

        Mounted.Add(isoPath);
        return Task.FromResult(Root);
    }

    public Task DismountAsync(string isoPath, CancellationToken ct)
    {
        if (ThrowOnDismount is not null)
        {
            return Task.FromException(ThrowOnDismount);
        }

        Dismounted.Add(isoPath);
        return Task.CompletedTask;
    }
}
