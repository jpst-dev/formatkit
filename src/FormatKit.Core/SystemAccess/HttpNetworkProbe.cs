using System.Net;

namespace FormatKit.Core.SystemAccess;

public sealed class HttpNetworkProbe(HttpMessageHandler? handler = null) : INetworkProbe
{
    private const string ProbeUrl = "http://www.msftconnecttest.com/connecttest.txt";
    private const string ExpectedBody = "Microsoft Connect Test";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public async Task<bool> IsOnlineAsync(CancellationToken ct)
    {
        try
        {
            using var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            client.Timeout = RequestTimeout;
            using var response = await client.GetAsync(ProbeUrl, ct).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return false;
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return body == ExpectedBody;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Includes HttpClient timeouts (TaskCanceledException): offline, not user cancellation.
            return false;
        }
    }
}
