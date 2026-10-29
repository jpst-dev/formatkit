using System.Text;
using FormatKit.App.Worker;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using Serilog;

namespace FormatKit.App.Tests;

public sealed class PipeInteractiveConsoleTests
{
    private static ILogger Log => new LoggerConfiguration().CreateLogger();

    private static async Task<List<WorkerMessage>> Sent(MemoryStream stream, MessageChannel channel)
    {
        await channel.DrainAsync();
        var copy = new MemoryStream(stream.ToArray());
        await using var reader = new MessageChannel(copy, Log);
        var list = new List<WorkerMessage>();
        await foreach (var m in reader.ReadAllAsync(CancellationToken.None)) list.Add(m);
        return list;
    }

    [Fact]
    public async Task RunAttached_TellsTheWindowBeforeAndAfter_WithoutTheArguments()
    {
        var stream = new MemoryStream();
        await using var channel = new MessageChannel(stream, Log);
        var inner = new FakeInteractiveConsole { ExitCode = _ => 3 };
        var console = new PipeInteractiveConsole(inner, channel);

        var exit = await console.RunAttachedAsync(
            new ProcessSpec("git", ["clone", "https://secret.example/repo"], TimeSpan.FromMinutes(1)), CancellationToken.None);

        Assert.Equal(3, exit);
        Assert.Single(inner.Attached);
        var sent = await Sent(stream, channel);
        Assert.Collection(sent, m => Assert.IsType<ConsoleOpened>(m), m => Assert.IsType<ConsoleClosed>(m));
        Assert.DoesNotContain("secret", Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public async Task RunAttachedThrowing_StillSendsClosed()
    {
        var stream = new MemoryStream();
        await using var channel = new MessageChannel(stream, Log);
        var inner = new FakeInteractiveConsole { ExitCode = _ => throw new InvalidOperationException("no exe") };
        var console = new PipeInteractiveConsole(inner, channel);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            console.RunAttachedAsync(new ProcessSpec("x", [], TimeSpan.FromMinutes(1)), CancellationToken.None));

        var sent = await Sent(stream, channel);
        Assert.IsType<ConsoleClosed>(sent[^1]);
    }

    [Fact]
    public async Task WriteLine_GoesToTheInnerConsoleAndTheWindow()
    {
        var stream = new MemoryStream();
        await using var channel = new MessageChannel(stream, Log);
        var inner = new FakeInteractiveConsole();
        var console = new PipeInteractiveConsole(inner, channel);

        console.WriteLine("ssh-ed25519 AAAA me@pc");

        Assert.Equal(["ssh-ed25519 AAAA me@pc"], inner.Lines);
        var line = Assert.IsType<ConsoleLine>(Assert.Single(await Sent(stream, channel)));
        Assert.Equal("ssh-ed25519 AAAA me@pc", line.Text);
    }

    [Fact]
    public void CanPrompt_ComesFromTheInnerConsole()
    {
        using var stream = new MemoryStream();
        var channel = new MessageChannel(stream, Log);

        Assert.False(new PipeInteractiveConsole(new FakeInteractiveConsole { CanPrompt = false }, channel).CanPrompt);
        Assert.True(new PipeInteractiveConsole(new FakeInteractiveConsole { CanPrompt = true }, channel).CanPrompt);
    }
}
