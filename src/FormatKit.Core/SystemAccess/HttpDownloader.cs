namespace FormatKit.Core.SystemAccess;

public sealed class HttpDownloader : IHttpDownloader
{
    private static readonly HttpClient SharedClient = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly HttpClient _client;

    public HttpDownloader() => _client = SharedClient;

    internal HttpDownloader(HttpMessageHandler handler) =>
        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

    public async Task DownloadAsync(Uri url, string destination, TimeSpan timeout, CancellationToken ct)
    {
        if (url.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException($"Only https downloads are allowed: {url}", nameof(url));

        var part = destination + ".part";

        // The download directory may be writable by standard users: never reuse a file someone else created there.
        // Stale files are deleted first (this throws if that is refused), then the new file must be created by us.
        File.Delete(part);
        File.Delete(destination);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false))
            await using (var target = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(target, cts.Token).ConfigureAwait(false);
            }

            File.Move(part, destination, overwrite: false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryDelete(part);
            throw new TimeoutException($"Download timed out after {timeout}: {url}");
        }
        catch
        {
            TryDelete(part);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
