using System.Net;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.SystemAccess;

public class HttpDownloaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "formatkit-http-" + Guid.NewGuid().ToString("N"));
    private static readonly Uri Url = new("https://example.com/setup.exe");

    public HttpDownloaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Dest => Path.Combine(_dir, "file.exe");

    private sealed class FakeHandler(Func<CancellationToken, HttpContent> content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content(ct) });
    }

    private sealed class ThrowingStream : Stream
    {
        private bool _first = true;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_first)
            {
                _first = false;
                buffer.Span[0] = 1;
                return new ValueTask<int>(1);
            }

            throw new IOException("connection reset");
        }
    }

    private sealed class HangingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    [Fact]
    public async Task Success_creates_destination_and_no_part_file()
    {
        var downloader = new HttpDownloader(new FakeHandler(_ => new ByteArrayContent([1, 2, 3])));

        await downloader.DownloadAsync(Url, Dest, TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.Equal([1, 2, 3], File.ReadAllBytes(Dest));
        Assert.False(File.Exists(Dest + ".part"));
    }

    [Fact]
    public async Task Stream_failure_mid_copy_leaves_no_files_and_propagates()
    {
        var downloader = new HttpDownloader(new FakeHandler(_ => new StreamContent(new ThrowingStream())));

        await Assert.ThrowsAsync<IOException>(
            () => downloader.DownloadAsync(Url, Dest, TimeSpan.FromSeconds(30), CancellationToken.None));

        Assert.False(File.Exists(Dest + ".part"));
        Assert.False(File.Exists(Dest));
    }

    [Fact]
    public async Task Exceeding_timeout_throws_TimeoutException_and_leaves_no_part_file()
    {
        var downloader = new HttpDownloader(new FakeHandler(_ => new StreamContent(new HangingStream())));

        await Assert.ThrowsAsync<TimeoutException>(
            () => downloader.DownloadAsync(Url, Dest, TimeSpan.FromMilliseconds(100), CancellationToken.None));

        Assert.False(File.Exists(Dest + ".part"));
        Assert.False(File.Exists(Dest));
    }
}
