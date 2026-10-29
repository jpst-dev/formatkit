using FormatKit.App.Worker;

namespace FormatKit.App.Tests.Fakes;

/// <summary>
/// Records each start and plays the worker: accepts the pipe named after <c>--pipe</c> in-process, so the launcher can
/// connect. Returns null (UAC declined) when <see cref="Decline"/> is set.
/// </summary>
internal sealed class FakeElevationStarter : IElevationStarter
{
    public bool Decline { get; set; }

    public List<(string Exe, IReadOnlyList<string> Args, bool Runas)> Starts { get; } = [];

    /// <summary>The worker's end of the pipe of the last start.</summary>
    public Task<Stream?>? Accepted { get; private set; }

    public FakeStartedProcess? Process { get; private set; }

    public Task<IStartedProcess?> StartAsync(string exe, IReadOnlyList<string> args, bool runas)
    {
        var copy = args.ToList();
        Starts.Add((exe, copy, runas));
        if (Decline) return Task.FromResult<IStartedProcess?>(null);

        var pipe = copy[copy.IndexOf("--pipe") + 1];
        Accepted = WorkerPipe.AcceptAsync(pipe, TimeSpan.FromSeconds(10), CancellationToken.None);
        Process = new FakeStartedProcess();
        return Task.FromResult<IStartedProcess?>(Process);
    }
}

internal sealed class FakeStartedProcess : IStartedProcess
{
    private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<int> Exited => _exit.Task;

    public void Exit(int code) => _exit.TrySetResult(code);
}
