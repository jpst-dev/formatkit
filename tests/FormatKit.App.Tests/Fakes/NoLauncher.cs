using FormatKit.App.Worker;

namespace FormatKit.App.Tests.Fakes;

/// <summary>A launcher for pages that must not start a worker.</summary>
public sealed class NoLauncher : IWorkerLauncher
{
    public Task<WorkerConnection?> StartAsync(IReadOnlyList<string> job, bool elevate, CancellationToken ct) =>
        throw new InvalidOperationException("no worker is started here");
}
