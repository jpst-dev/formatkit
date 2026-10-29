using System.Text;
using FormatKit.App.Worker;
using FormatKit.Core.Running;
using FormatKit.Core.Steps;
using Serilog;

namespace FormatKit.App.Tests;

public class MessageChannelTests
{
    public static TheoryData<WorkerMessage> AllMessages() => new()
    {
        new Planned([new PlannedStep("a", "Step A", true), new PlannedStep("b", "Step B", false)]),
        new StepStarted(1, 2, "a"),
        new StepFinished("a", StepOutcome.RebootRequired, ["m1", "m2"], "do this"),
        new StepFinished("b", StepOutcome.Succeeded, [], null),
        new RebootCountdown(30),
        new Finished(new RunSummary(RunStatus.CompletedWithFailures,
            [new StepReport("a", "Step A", StepOutcome.Failed, ["x"], "retry")], "C:\\r", "err", true)),
        new Failed("boom"),
        new Cancel(),
        new RebootNow(),
        new RebootCancel(),
    };

    private static ILogger Log => new LoggerConfiguration().CreateLogger();

    private static MessageChannel Over(string text) =>
        new(new MemoryStream(new UTF8Encoding(false).GetBytes(text)), Log);

    private static async Task<List<WorkerMessage>> ReadAll(MessageChannel c)
    {
        var list = new List<WorkerMessage>();
        await foreach (var m in c.ReadAllAsync(CancellationToken.None)) list.Add(m);
        return list;
    }

    [Theory]
    [MemberData(nameof(AllMessages))]
    public async Task RoundTrip_EveryMessageType(WorkerMessage message)
    {
        var ms = new MemoryStream();
        await using var ch = new MessageChannel(ms, Log);
        await ch.SendAsync(message, CancellationToken.None);
        ms.Position = 0;
        var read = await ReadAll(ch);
        var back = Assert.Single(read);
        Assert.Equal(message.GetType(), back.GetType());
        Assert.Equal(await Serialize(message), await Serialize(back));
    }

    // Records hold lists, so compare through the serialized form.
    private static async Task<string> Serialize(WorkerMessage m)
    {
        var ms = new MemoryStream();
        await using var ch = new MessageChannel(ms, Log);
        await ch.SendAsync(m, CancellationToken.None);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    [Fact]
    public async Task Send_WritesOneLinePerMessage()
    {
        var ms = new MemoryStream();
        await using var ch = new MessageChannel(ms, Log);
        await ch.SendAsync(new Failed("a\nb"), CancellationToken.None);
        await ch.SendAsync(new RebootNow(), CancellationToken.None);
        var bytes = ms.ToArray();
        Assert.False(bytes.Length > 2 && bytes[0] == 0xEF && bytes[1] == 0xBB);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("\r", text);
        Assert.EndsWith("\n", text);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"type\":\"failed\"", lines[0]);
        Assert.Contains("\"type\":\"rebootNow\"", lines[1]);
    }

    [Fact]
    public async Task Send_EnumsAreStringsAndNamesCamelCase()
    {
        var ms = new MemoryStream();
        await using var ch = new MessageChannel(ms, Log);
        await ch.SendAsync(new StepFinished("a", StepOutcome.Skipped, [], null), CancellationToken.None);
        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("\"outcome\":\"Skipped\"", text);
        Assert.Contains("\"nextAction\"", text);
    }

    [Fact]
    public async Task Read_GarbageLine_SkippedAndNextRead()
    {
        await using var ch = Over("not json\n\n{\"type\":\"rebootNow\"}\n{broken\n{\"type\":\"cancel\"}\n");
        var read = await ReadAll(ch);
        Assert.Equal(2, read.Count);
        Assert.IsType<RebootNow>(read[0]);
        Assert.IsType<Cancel>(read[1]);
    }

    [Fact]
    public async Task Read_UnknownType_Skipped()
    {
        await using var ch = Over("{\"type\":\"nope\",\"x\":1}\n{\"type\":\"cancel\"}\n");
        var read = await ReadAll(ch);
        Assert.IsType<Cancel>(Assert.Single(read));
    }

    [Fact]
    public async Task Read_PartialLastLineThenClose_EndsCleanly()
    {
        await using var ch = Over("{\"type\":\"cancel\"}\n{\"type\":\"failed\",\"mess");
        var read = await ReadAll(ch);
        Assert.IsType<Cancel>(Assert.Single(read));
    }

    [Fact]
    public async Task Read_LineOverCap_SkippedAndNextRead()
    {
        var big = "{\"type\":\"failed\",\"message\":\"" + new string('x', MessageChannel.MaxLineBytes + 10) + "\"}";
        await using var ch = Over(big + "\n{\"type\":\"cancel\"}\n");
        var read = await ReadAll(ch);
        Assert.IsType<Cancel>(Assert.Single(read));
    }

    [Fact]
    public async Task Send_Concurrent_LinesNeverInterleave()
    {
        var ms = new MemoryStream();
        await using var ch = new MessageChannel(ms, Log);
        var msg = new string('y', 5000);
        await Task.WhenAll(Enumerable.Range(0, 200).Select(i =>
            Task.Run(() => ch.SendAsync(new Failed(msg + i), CancellationToken.None))));
        var lines = Encoding.UTF8.GetString(ms.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(200, lines.Length);
        ms.Position = 0;
        var read = await ReadAll(ch);
        Assert.Equal(200, read.Count);
        Assert.All(read, m => Assert.StartsWith(msg, Assert.IsType<Failed>(m).Message));
    }
}
