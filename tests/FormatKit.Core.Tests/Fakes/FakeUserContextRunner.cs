using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeUserContextRunner : IUserContextRunner
{
    public List<ProcessSpec> Calls { get; } = [];

    public ProcessResult Result { get; set; } = new(0, string.Empty, string.Empty, TimeSpan.Zero, false);

    public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct)
    {
        Calls.Add(spec);
        return Task.FromResult(Result);
    }
}
