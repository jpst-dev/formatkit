using System.Net;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.SystemAccess;

public class HttpNetworkProbeTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }

    private static HttpNetworkProbe Probe(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) =>
        new(new StubHandler(respond));

    private static Task<HttpResponseMessage> Ok(string body) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });

    [Fact]
    public async Task Online_when_body_matches()
    {
        Uri? requested = null;
        var probe = Probe(r => { requested = r.RequestUri; return Ok("Microsoft Connect Test"); });

        Assert.True(await probe.IsOnlineAsync(TestContext.Current.CancellationToken));
        Assert.Equal("http://www.msftconnecttest.com/connecttest.txt", requested!.ToString());
    }

    [Fact]
    public async Task Offline_when_body_differs()
    {
        var probe = Probe(_ => Ok("<html>Please sign in</html>"));

        Assert.False(await probe.IsOnlineAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Offline_on_exception()
    {
        var probe = Probe(_ => throw new HttpRequestException("no network"));

        Assert.False(await probe.IsOnlineAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Offline_on_timeout_style_cancellation()
    {
        var probe = Probe(_ => throw new TaskCanceledException("timeout"));

        Assert.False(await probe.IsOnlineAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Caller_cancellation_is_rethrown()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var probe = Probe(_ => throw new TaskCanceledException("cancelled"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.IsOnlineAsync(cts.Token));
    }
}
