using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Dictionary<string, Queue<ProcessResult>> _results = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _notFound = new(StringComparer.OrdinalIgnoreCase);

    public List<ProcessSpec> Calls { get; } = [];

    public void Enqueue(string fileName, ProcessResult result)
    {
        if (!_results.TryGetValue(fileName, out var queue))
        {
            _results[fileName] = queue = new Queue<ProcessResult>();
        }

        queue.Enqueue(result);
    }

    public void ThrowNotFound(string fileName) => _notFound.Add(fileName);

    public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct)
    {
        Calls.Add(spec);

        if (_notFound.Contains(spec.FileName))
        {
            throw new FileNotFoundException($"Executable not found: {spec.FileName}", spec.FileName);
        }

        if (_results.TryGetValue(spec.FileName, out var queue) && queue.Count > 0)
        {
            return Task.FromResult(queue.Dequeue());
        }

        throw new InvalidOperationException($"No queued result for '{spec.FileName}'.");
    }
}
