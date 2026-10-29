using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace FormatKit.App.Worker;

/// <summary>
/// One JSON message per line (UTF-8 without BOM, <c>\n</c> endings) over a stream. Sends are serialized so lines
/// never interleave; the reader skips and logs garbage, unknown types and lines over <see cref="MaxLineBytes"/>.
/// </summary>
public sealed class MessageChannel(Stream stream, Serilog.ILogger log) : IAsyncDisposable
{
    public const int MaxLineBytes = 1024 * 1024;

    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public async Task SendAsync(WorkerMessage message, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message, MessageJsonContext.Default.WorkerMessage);
        var line = new byte[json.Length + 1];
        json.CopyTo(line, 0);
        line[^1] = (byte)'\n';

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Once the lock is held the line goes out whole: a cancelled write would leave half a line on the stream.
            await stream.WriteAsync(line, CancellationToken.None).ConfigureAwait(false);
            await stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async IAsyncEnumerable<WorkerMessage> ReadAllAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var line = new MemoryStream();
        var skipping = false;

        while (true)
        {
            // WaitAsync: a stream that ignores the token still lets the reader end when it is cancelled.
            var read = await stream.ReadAsync(buffer, ct).AsTask().WaitAsync(ct).ConfigureAwait(false);
            if (read == 0)
            {
                // End of stream: a final line without a newline is parsed if complete, dropped if partial.
                if (!skipping && line.Length > 0 && TryParse(line, out var last)) yield return last;
                yield break;
            }

            var start = 0;
            while (start < read)
            {
                var newline = Array.IndexOf(buffer, (byte)'\n', start, read - start);
                var end = newline < 0 ? read : newline;

                if (!skipping)
                {
                    if (line.Length + (end - start) > MaxLineBytes)
                    {
                        log.Warning("Worker channel: line over {Max} bytes skipped", MaxLineBytes);
                        skipping = true;
                        line.SetLength(0);
                    }
                    else
                    {
                        line.Write(buffer, start, end - start);
                    }
                }

                if (newline < 0) break;

                if (!skipping && line.Length > 0 && TryParse(line, out var message)) yield return message;
                line.SetLength(0);
                skipping = false;
                start = newline + 1;
            }
        }
    }

    private bool TryParse(MemoryStream line, out WorkerMessage message)
    {
        message = null!;
        try
        {
            var span = new ReadOnlySpan<byte>(line.GetBuffer(), 0, (int)line.Length);
            if (span.Length > 0 && span[^1] == (byte)'\r') span = span[..^1];
            var parsed = JsonSerializer.Deserialize(span, MessageJsonContext.Default.WorkerMessage);
            if (parsed is null) return false;
            message = parsed;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            log.Warning("Worker channel: line skipped ({Reason})", ex.Message);
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _sendLock.Dispose();
        await stream.DisposeAsync().ConfigureAwait(false);
    }
}
