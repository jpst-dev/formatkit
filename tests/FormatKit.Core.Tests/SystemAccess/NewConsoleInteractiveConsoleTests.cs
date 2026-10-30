using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace FormatKit.Core.Tests.SystemAccess;

[Trait("Category", "Process")]
public class NewConsoleInteractiveConsoleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ILogger Log => new LoggerConfiguration().CreateLogger();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NewConsole_CanPrompt_FollowsInteractiveUser(bool interactive)
    {
        var console = new NewConsoleInteractiveConsole(new FakeUserSession { HasInteractiveUser = interactive }, Log);

        Assert.Equal(interactive, console.CanPrompt);
    }

    [Fact]
    public async Task RunAttached_ReturnsTheExitCode()
    {
        var console = new NewConsoleInteractiveConsole(new FakeUserSession(), Log);

        var code = await console.RunAttachedAsync(new ProcessSpec("cmd.exe", ["/c", "exit 3"], TimeSpan.FromSeconds(10)), Ct);

        Assert.Equal(3, code);
    }

    [Fact]
    public async Task RunAttached_PassesTheEnvironment()
    {
        var console = new NewConsoleInteractiveConsole(new FakeUserSession(), Log);
        var spec = new ProcessSpec("cmd.exe", ["/c", "if \"%FK_TEST_VALUE%\"==\"fk-42\" (exit 5) else (exit 1)"], TimeSpan.FromSeconds(10),
            new Dictionary<string, string> { ["FK_TEST_VALUE"] = "fk-42" });

        Assert.Equal(5, await console.RunAttachedAsync(spec, Ct));
    }

    [Fact]
    public async Task RunAttached_KillsAfterTheTimeout()
    {
        var console = new NewConsoleInteractiveConsole(new FakeUserSession(), Log);

        var started = DateTime.UtcNow;
        await console.RunAttachedAsync(
            new ProcessSpec("powershell.exe", ["-NoProfile", "-Command", "Start-Sleep 30"], TimeSpan.FromSeconds(1)), Ct);

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task RunAttached_MissingExecutable_ThrowsFileNotFound()
    {
        var console = new NewConsoleInteractiveConsole(new FakeUserSession(), Log);

        await Assert.ThrowsAsync<FileNotFoundException>(() => console.RunAttachedAsync(
            new ProcessSpec(@"C:\does-not-exist\nope.exe", [], TimeSpan.FromSeconds(5)), Ct));
    }

    [Fact]
    public void WriteLine_GoesToTheLog()
    {
        var sink = new ListSink();
        var console = new NewConsoleInteractiveConsole(new FakeUserSession(), new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger());

        console.WriteLine("Type the passphrase in the window that opened");

        var e = Assert.Single(sink.Events);
        Assert.Contains("Type the passphrase in the window that opened", e.RenderMessage());
    }

    [Fact]
    public async Task RunAttached_LogsOnlyTheFileName()
    {
        var sink = new ListSink();
        var console = new NewConsoleInteractiveConsole(new FakeUserSession(), new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger());

        await console.RunAttachedAsync(new ProcessSpec("cmd.exe", ["/c", "exit 0", "secret-argument"], TimeSpan.FromSeconds(10)), Ct);

        Assert.NotEmpty(sink.Events);
        Assert.DoesNotContain(sink.Events, e => e.RenderMessage().Contains("secret-argument"));
    }

    private sealed class ListSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            lock (Events) Events.Add(logEvent);
        }
    }
}
