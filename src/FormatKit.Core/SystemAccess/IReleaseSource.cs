namespace FormatKit.Core.SystemAccess;

public sealed record ReleaseAsset(string Name, Uri Url, long Size);

/// <param name="Page">The release's web page.</param>
public sealed record ReleaseInfo(string Tag, Uri Page, IReadOnlyList<ReleaseAsset> Assets);

public interface IReleaseSource
{
    /// <summary>
    /// The latest stable release. Throws <see cref="HttpRequestException"/>, <see cref="TimeoutException"/> or
    /// <see cref="InvalidDataException"/> when it cannot be read.
    /// </summary>
    Task<ReleaseInfo> GetLatestAsync(CancellationToken ct);
}
