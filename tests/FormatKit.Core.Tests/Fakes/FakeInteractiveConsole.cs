using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeInteractiveConsole : IInteractiveConsole
{
    public bool CanPrompt { get; set; } = true;

    public List<ProcessSpec> Attached { get; } = [];

    public Func<ProcessSpec, int> ExitCode { get; set; } = _ => 0;

    public List<string> Lines { get; } = [];

    public Task<int> RunAttachedAsync(ProcessSpec spec, CancellationToken ct)
    {
        Attached.Add(spec);
        return Task.FromResult(ExitCode(spec));
    }

    public void WriteLine(string text) => Lines.Add(text);
}
