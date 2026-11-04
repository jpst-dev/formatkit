using System.Text.Json;

namespace FormatKit.Core.SystemAccess;

/// <summary>Asks GitHub for the latest stable release. It sends the app's version and nothing about the user.</summary>
public sealed class GitHubReleaseSource : IReleaseSource
{
    private const string LatestUrl = "https://api.github.com/repos/jpst-dev/formatkit/releases/latest";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private static readonly HttpClient SharedClient = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private readonly HttpClient _client;
    private readonly string _version;

    public GitHubReleaseSource(string version)
    {
        _client = SharedClient;
        _version = version;
    }

    internal GitHubReleaseSource(string version, HttpMessageHandler handler)
    {
        _client = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        _version = version;
    }

    public async Task<ReleaseInfo> GetLatestAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", $"FormatKit/{_version}");
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        try
        {
            using var response = await _client.SendAsync(request, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return Parse(await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"No answer from GitHub after {Timeout.TotalSeconds} s.");
        }
    }

    private static ReleaseInfo Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var assets = root.GetProperty("assets").EnumerateArray().Select(a => new ReleaseAsset(
                a.GetProperty("name").GetString() ?? throw new InvalidDataException("An asset has no name."),
                Address(a.GetProperty("browser_download_url")),
                a.GetProperty("size").GetInt64())).ToList();
            var tag = root.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("The release has no tag.");
            return new ReleaseInfo(tag, Address(root.GetProperty("html_url")), assets);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("GitHub's answer was not understood.", ex);
        }
    }

    private static Uri Address(JsonElement element) =>
        Uri.TryCreate(element.GetString(), UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidDataException("The release has an invalid address.");
}
