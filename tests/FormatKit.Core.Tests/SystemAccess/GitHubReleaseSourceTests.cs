using System.Net;
using System.Text;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.SystemAccess;

public sealed class GitHubReleaseSourceTests
{
    private const string Sample = """
        {
          "tag_name": "v0.3.0",
          "html_url": "https://github.com/jpst-dev/formatkit/releases/tag/v0.3.0",
          "assets": [
            { "name": "formatkit.exe", "size": 12, "browser_download_url": "https://github.com/jpst-dev/formatkit/releases/download/v0.3.0/formatkit.exe" },
            { "name": "SHA256SUMS.txt", "size": 3, "browser_download_url": "https://github.com/jpst-dev/formatkit/releases/download/v0.3.0/SHA256SUMS.txt" }
          ]
        }
        """;

    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) });
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reads_the_tag_page_and_assets()
    {
        var release = await new GitHubReleaseSource("0.2.1", new Handler(HttpStatusCode.OK, Sample)).GetLatestAsync(Ct);

        Assert.Equal("v0.3.0", release.Tag);
        Assert.Equal("https://github.com/jpst-dev/formatkit/releases/tag/v0.3.0", release.Page.AbsoluteUri);
        Assert.Equal(["formatkit.exe", "SHA256SUMS.txt"], release.Assets.Select(a => a.Name));
        Assert.Equal(12, release.Assets[0].Size);
        Assert.EndsWith("/v0.3.0/formatkit.exe", release.Assets[0].Url.AbsoluteUri);
    }

    [Fact]
    public async Task Asks_the_latest_stable_release_and_names_only_the_app()
    {
        var handler = new Handler(HttpStatusCode.OK, Sample);

        await new GitHubReleaseSource("0.2.1", handler).GetLatestAsync(Ct);

        Assert.Equal("https://api.github.com/repos/jpst-dev/formatkit/releases/latest", handler.Request!.RequestUri!.AbsoluteUri);
        Assert.Equal("FormatKit/0.2.1", handler.Request.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task A_bad_status_throws() =>
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new GitHubReleaseSource("0.2.1", new Handler(HttpStatusCode.Forbidden, "{}")).GetLatestAsync(Ct));

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "tag_name": "v1.0.0" }""")]
    [InlineData("""{ "tag_name": 5, "html_url": "https://x/", "assets": [] }""")]
    [InlineData("""{ "tag_name": "v1.0.0", "html_url": "nope", "assets": [] }""")]
    [InlineData("""{ "tag_name": "v1.0.0", "html_url": "https://x/", "assets": [ { "name": "a" } ] }""")]
    public async Task An_unexpected_answer_throws_invalid_data(string body) =>
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new GitHubReleaseSource("0.2.1", new Handler(HttpStatusCode.OK, body)).GetLatestAsync(Ct));
}
