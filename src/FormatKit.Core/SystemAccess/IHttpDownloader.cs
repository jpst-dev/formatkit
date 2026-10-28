namespace FormatKit.Core.SystemAccess;

public interface IHttpDownloader
{
    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="destination"/>. Throws <see cref="IOException"/>,
    /// <see cref="HttpRequestException"/> or <see cref="TimeoutException"/> on failure and removes any partial file first.
    /// </summary>
    Task DownloadAsync(Uri url, string destination, TimeSpan timeout, CancellationToken ct);
}
