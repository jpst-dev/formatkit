using FormatKit.App.Worker;

namespace FormatKit.App.Tests.Fakes;

/// <summary>Waits for the window to send a message of type <typeparamref name="T"/>.</summary>
public sealed record Expect<T> : IScriptItem where T : WorkerMessage
{
    public Task RunAsync(FakeWorkerLauncher worker) => worker.WaitForAsync<T>();
}

/// <summary>Runs an action at this point of the script (seed the run state, wait on a gate).</summary>
public sealed record Do(Func<Task> Action) : IScriptItem
{
    public Do(Action action) : this(() => { action(); return Task.CompletedTask; }) { }

    public Task RunAsync(FakeWorkerLauncher worker) => Action();
}

public interface IScriptItem
{
    Task RunAsync(FakeWorkerLauncher worker);
}

/// <summary>
/// A worker that replays a script over an in-memory pipe: <see cref="WorkerMessage"/> items are sent to the window,
/// <see cref="Expect{T}"/> waits for a message from it, <see cref="Do"/> runs an action. When the script ends the
/// worker closes its end, as a worker that exits does. Records the jobs it was started with and what the window sent.
/// </summary>
public sealed class FakeWorkerLauncher : IWorkerLauncher
{
    private readonly Lock _gate = new();
    private readonly List<WorkerMessage> _received = [];
    private TaskCompletionSource _messageArrived = NewSignal();

    /// <summary>Null as the result of the start: the UAC prompt was declined.</summary>
    public bool Declines { get; set; }

    public List<object> Script { get; } = [];

    public List<IReadOnlyList<string>> Jobs { get; } = [];

    public List<bool> Elevations { get; } = [];

    /// <summary>Completes when the script has run and the worker's end is closed.</summary>
    public Task Done { get; private set; } = Task.CompletedTask;

    public IReadOnlyList<WorkerMessage> Received
    {
        get
        {
            lock (_gate) return [.. _received];
        }
    }

    /// <summary>When set, the start waits for it (a pending UAC prompt).</summary>
    public Task? StartGate { get; set; }

    public async Task<WorkerConnection?> StartAsync(IReadOnlyList<string> job, bool elevate, CancellationToken ct)
    {
        Jobs.Add(job);
        Elevations.Add(elevate);
        if (StartGate is { } gate)
            await gate;
        if (Declines)
            return null;

        var (workerEnd, windowEnd) = DuplexStream.CreatePair();
        var log = new Serilog.LoggerConfiguration().CreateLogger();
        var worker = new MessageChannel(workerEnd, log);
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(() => ReadAsync(worker));
        Done = Task.Run(async () =>
        {
            try
            {
                await RunScriptAsync(worker);
            }
            finally
            {
                await workerEnd.DisposeAsync();
                exited.TrySetResult(0);
            }
        });
        return new WorkerConnection(new MessageChannel(windowEnd, log), exited.Task);
    }

    public async Task WaitForAsync<T>() where T : WorkerMessage
    {
        while (true)
        {
            Task arrived;
            lock (_gate)
            {
                if (_received.OfType<T>().Any()) return;
                arrived = _messageArrived.Task;
            }

            await arrived;
        }
    }

    private async Task RunScriptAsync(MessageChannel worker)
    {
        foreach (var item in Script)
        {
            switch (item)
            {
                case WorkerMessage message:
                    await worker.SendAsync(message, CancellationToken.None);
                    break;
                case IScriptItem step:
                    await step.RunAsync(this);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown script item {item}");
            }
        }
    }

    private async Task ReadAsync(MessageChannel worker)
    {
        try
        {
            await foreach (var message in worker.ReadAllAsync(CancellationToken.None))
            {
                lock (_gate)
                {
                    _received.Add(message);
                    _messageArrived.TrySetResult();
                    _messageArrived = NewSignal();
                }
            }
        }
        catch (Exception)
        {
            // The worker's end was closed while reading: the run is over.
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
