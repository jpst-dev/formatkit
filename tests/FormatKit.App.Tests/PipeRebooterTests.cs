using FormatKit.App.Tests.Fakes;
using FormatKit.App.Worker;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using Serilog;

namespace FormatKit.App.Tests;

public sealed class PipeRebooterTests : IAsyncDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly FakeProcessRunner _runner = new();
    private readonly MessageChannel _worker;
    private readonly MessageChannel _window;
    private readonly PipeRebooter _rebooter;

    public PipeRebooterTests()
    {
        var (workerEnd, windowEnd) = DuplexStream.CreatePair();
        var log = new LoggerConfiguration().CreateLogger();
        _worker = new MessageChannel(workerEnd, log);
        _window = new MessageChannel(windowEnd, log);
        _rebooter = new PipeRebooter(_worker, _runner, log);
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _window.DisposeAsync();
    }

    private static ProcessResult Ok => new(0, "", "", TimeSpan.Zero, false);

    /// <summary>Waits for the countdown on the window side, then delivers <paramref name="reply"/> as the read loop would.</summary>
    private async Task<RebootCountdown> AnswerAsync(WorkerMessage reply)
    {
        await using var e = _window.ReadAllAsync(Ct).GetAsyncEnumerator(Ct);
        Assert.True(await e.MoveNextAsync().AsTask().WaitAsync(Wait, Ct));
        var countdown = Assert.IsType<RebootCountdown>(e.Current);
        _rebooter.Deliver(reply);
        return countdown;
    }

    [Fact]
    public async Task PipeRebooter_Cancel_ReturnsFalse()
    {
        var reboot = _rebooter.RebootAsync(TimeSpan.FromMinutes(1), Ct);
        var countdown = await AnswerAsync(new RebootCancel());

        Assert.False(await reboot.WaitAsync(Wait, Ct));
        Assert.Equal(60, countdown.Seconds);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task PipeRebooter_RebootNow_CallsShutdown()
    {
        _runner.Enqueue("shutdown.exe", Ok);
        var reboot = _rebooter.RebootAsync(TimeSpan.FromMinutes(1), Ct);
        await AnswerAsync(new RebootNow());

        Assert.True(await reboot.WaitAsync(Wait, Ct));
        var call = Assert.Single(_runner.Calls);
        Assert.Equal("shutdown.exe", call.FileName);
        Assert.Equal(["/r", "/t", "0"], call.Arguments);
    }

    [Fact]
    public async Task PipeRebooter_CountdownElapsed_CallsShutdown()
    {
        _runner.Enqueue("shutdown.exe", Ok);

        Assert.True(await _rebooter.RebootAsync(TimeSpan.FromMilliseconds(50), Ct));

        Assert.Equal("shutdown.exe", Assert.Single(_runner.Calls).FileName);
        await using var e = _window.ReadAllAsync(Ct).GetAsyncEnumerator(Ct);
        Assert.True(await e.MoveNextAsync().AsTask().WaitAsync(Wait, Ct));
        Assert.Equal(1, Assert.IsType<RebootCountdown>(e.Current).Seconds);
    }

    [Fact]
    public async Task PipeRebooter_ShutdownFails_ReturnsFalse()
    {
        _runner.Enqueue("shutdown.exe", new ProcessResult(1, "", "denied", TimeSpan.Zero, false));
        var reboot = _rebooter.RebootAsync(TimeSpan.FromMinutes(1), Ct);
        await AnswerAsync(new RebootNow());

        Assert.False(await reboot.WaitAsync(Wait, Ct));
    }

    [Fact]
    public async Task PipeRebooter_ReplyWithNoCountdownPending_IsIgnored()
    {
        _rebooter.Deliver(new RebootNow());
        var reboot = _rebooter.RebootAsync(TimeSpan.FromMinutes(1), Ct);
        await AnswerAsync(new RebootCancel());

        Assert.False(await reboot.WaitAsync(Wait, Ct));
        Assert.Empty(_runner.Calls);
    }
}
