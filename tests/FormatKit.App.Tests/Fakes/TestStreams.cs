using System.IO.Pipelines;

namespace FormatKit.App.Tests.Fakes;

/// <summary>Writes in small chunks with a yield between them, so interleaved writes would show; honors the token per chunk.</summary>
internal sealed class ChunkedWriteStream : Stream
{
    private readonly MemoryStream _inner = new();

    /// <summary>Runs after each chunk is written.</summary>
    public Action? AfterChunk { get; set; }

    public byte[] ToArray() => _inner.ToArray();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        for (var i = 0; i < buffer.Length; i += 7)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = buffer.Slice(i, Math.Min(7, buffer.Length - i));
            lock (_inner) _inner.Write(chunk.Span);
            AfterChunk?.Invoke();
            await Task.Yield();
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override Task FlushAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Returns at most 1-3 bytes per read, cycling, from a fixed buffer.</summary>
internal sealed class TrickleReadStream(byte[] data) : Stream
{
    private int _position;
    private int _turn;

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var n = Math.Min(Math.Min(buffer.Length, 1 + (_turn++ % 3)), data.Length - _position);
        data.AsSpan(_position, n).CopyTo(buffer.Span);
        _position += n;
        return ValueTask.FromResult(n);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>A read that never completes and ignores its token.</summary>
internal sealed class NeverReadStream : Stream
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
        new(new TaskCompletionSource<int>().Task);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        new TaskCompletionSource<int>().Task;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Reads from one stream and writes to another: one end of an in-memory duplex connection.</summary>
internal sealed class DuplexStream(Stream read, Stream write) : Stream
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => read.ReadAsync(buffer, ct);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        read.ReadAsync(buffer, offset, count, ct);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => write.WriteAsync(buffer, ct);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        write.WriteAsync(buffer, offset, count, ct);

    public override Task FlushAsync(CancellationToken ct) => write.FlushAsync(ct);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            read.Dispose();
            write.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await read.DisposeAsync();
        await write.DisposeAsync();
        await base.DisposeAsync();
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => write.Flush();
    public override int Read(byte[] buffer, int offset, int count) => read.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => write.Write(buffer, offset, count);

    /// <summary>
    /// Two connected ends over a pair of <see cref="Pipe"/>s. Disposing one end completes its writer, which the other
    /// end reads as end of stream.
    /// </summary>
    public static (DuplexStream Worker, DuplexStream Window) CreatePair()
    {
        var toWorker = new Pipe();
        var toWindow = new Pipe();
        return (
            new DuplexStream(toWorker.Reader.AsStream(), toWindow.Writer.AsStream()),
            new DuplexStream(toWindow.Reader.AsStream(), toWorker.Writer.AsStream()));
    }
}
