using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeVolumes(params VolumeInfo[] volumes) : IVolumes
{
    public List<VolumeInfo> Volumes { get; } = [.. volumes];

    /// <summary>When set, <see cref="ListAsync"/> throws it.</summary>
    public Exception? Throw { get; set; }

    public Task<IReadOnlyList<VolumeInfo>> ListAsync(CancellationToken ct) =>
        Throw is null ? Task.FromResult<IReadOnlyList<VolumeInfo>>([.. Volumes]) : Task.FromException<IReadOnlyList<VolumeInfo>>(Throw);
}
