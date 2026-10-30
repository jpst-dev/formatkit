using System.IO.Pipes;
using FormatKit.App.Worker;
using FormatKit.Core;
using FormatKit.Core.Running;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace FormatKit.App.Tests;

public class WorkerProcessTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static int Me => Environment.ProcessId;

    private static readonly string[] Resume = ["run", "--resume"];

    private readonly ListSink _sink = new();
    private readonly HostEnvironment _host;

    public WorkerProcessTests()
    {
        var log = new LoggerConfiguration().WriteTo.Sink(_sink).CreateLogger();
        var files = new InMemoryFileSystem();
        _host = new HostEnvironment(
            TestServices.Create(files: files),
            new Elevation(),
            new FakeScheduledTasks(),
            new FakeRebooter(),
            new FakeReportWriter(),
            new RunStateStore(files, new AppPaths(@"C:\FormatKitTest")),
            log,
            TimeSpan.Zero,
            TimeSpan.Zero);
    }

    private static string NewName() => "FormatKit-test-" + Guid.NewGuid().ToString("N");

    private Task<int> Run(string name, int clientPid, TimeSpan timeout) =>
        WorkerProcess.RunAsync(_host, [], name, clientPid, Resume, timeout, Ct);

    [Fact]
    public async Task NameAlreadyTaken_LogsAndFails()
    {
        var name = NewName();
        await using var squatter = new NamedPipeServerStream(
            name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        var code = await Run(name, Me, TimeSpan.FromSeconds(5));

        Assert.Equal(WorkerHost.ExitFailed, code);
        Assert.Contains(_sink.Events, e => e.Level >= LogEventLevel.Error && e.Exception is not null);
    }

    [Fact]
    public async Task NoClient_LogsAndFails()
    {
        var code = await Run(NewName(), Me, TimeSpan.FromMilliseconds(100));

        Assert.Equal(WorkerHost.ExitFailed, code);
        Assert.Contains(_sink.Events, e => e.Level == LogEventLevel.Error && e.RenderMessage().Contains("no window connected"));
    }

    [Fact]
    public async Task WrongClientProcess_LogsClosesAndDoesNotRunTheJob()
    {
        var name = NewName();
        var running = Run(name, Me + 4, TimeSpan.FromSeconds(10));
        await using var client = new MessageChannel(await WorkerPipe.ConnectAsync(name, TimeSpan.FromSeconds(10), Ct), _host.Log);

        Assert.Equal(WorkerHost.ExitFailed, await running.WaitAsync(TimeSpan.FromSeconds(10), Ct));

        var received = new List<WorkerMessage>();
        await foreach (var m in client.ReadAllAsync(Ct)) received.Add(m);
        Assert.Empty(received);
        Assert.Contains(_sink.Events, e => e.Level >= LogEventLevel.Error && e.Exception is UnauthorizedAccessException);
    }

    [Fact]
    public async Task RightClient_RunsTheJob()
    {
        var name = NewName();
        var running = Run(name, Me, TimeSpan.FromSeconds(10));
        await using var client = new MessageChannel(await WorkerPipe.ConnectAsync(name, TimeSpan.FromSeconds(10), Ct), _host.Log);

        await using var messages = client.ReadAllAsync(Ct).GetAsyncEnumerator(Ct);
        Assert.True(await messages.MoveNextAsync());
        var finished = Assert.IsType<Finished>(messages.Current);
        Assert.Equal(RunStatus.NothingToResume, finished.Summary.Status);
        Assert.Equal(WorkerHost.ExitFinished, await running.WaitAsync(TimeSpan.FromSeconds(10), Ct));
    }

    private sealed class ListSink : ILogEventSink
    {
        private readonly List<LogEvent> _events = [];

        public IReadOnlyList<LogEvent> Events
        {
            get
            {
                lock (_events) return _events.ToList();
            }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_events) _events.Add(logEvent);
        }
    }
}
