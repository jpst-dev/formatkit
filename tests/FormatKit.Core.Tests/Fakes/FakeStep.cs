using FormatKit.Core.Steps;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeStep(string id) : IStep
{
    public string Id { get; } = id;
    public string Title { get; set; } = id;
    public bool RequiresAdmin { get; set; }
    public bool IsCritical { get; set; }
    public bool CheckResult { get; set; } = true;
    public StepResult ExecuteResult { get; set; } = StepResult.Success();
    public Exception? Throws { get; set; }
    public int CheckCalls { get; private set; }
    public int ExecuteCalls { get; private set; }

    public Task<bool> CheckAsync(StepContext ctx, CancellationToken ct)
    {
        CheckCalls++;
        return Task.FromResult(CheckResult);
    }

    public Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        ExecuteCalls++;
        if (Throws is not null) throw Throws;
        return Task.FromResult(ExecuteResult);
    }
}
