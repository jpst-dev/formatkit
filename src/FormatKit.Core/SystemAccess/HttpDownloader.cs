namespace FormatKit.Core.SystemAccess;

public sealed class HttpDownloader : IHttpDownloader
{
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };

    public async Task DownloadAsync(Uri url, string destination, TimeSpan timeout, CancellationToken ct)
    {
        if (url.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException($"Only https downloads are allowed: {url}", nameof(url));

        var part = destination + ".part";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false))
            await using (var target = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(target, cts.Token).ConfigureAwait(false);
            }

            File.Move(part, destination, overwrite: true);
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
