using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeHttpDownloader(InMemoryFileSystem files) : IHttpDownloader
{
    public string Content { get; set; } = "installer-bytes";

    public Exception? Throws { get; set; }

    /// <summary>The token passed with the last call.</summary>
    public CancellationToken Token { get; private set; }

    public List<(Uri Url, string Destination, TimeSpan Timeout)> Calls { get; } = [];

    public Task DownloadAsync(Uri url, string destination, TimeSpan timeout, CancellationToken ct)
    {
        Calls.Add((url, destination, timeout));
        Token = ct;
        if (Throws is not null)
            throw Throws;
        files.Seed(destination, Content);
        return Task.CompletedTask;
    }
}
