using FormatKit.Core.Steps;

namespace FormatKit.Core.Running;

public sealed record StepRecord(
    StepOutcome Outcome,
    DateTimeOffset FinishedAt,
    IReadOnlyList<string> Messages,
    string? NextAction);

public sealed record RunState(
    string ProfilePath,
    string ProfileHash,
    IReadOnlyList<string> PlanStepIds,
    Dictionary<string, StepRecord> Results,
    DateTimeOffset StartedAt);

public enum StateLoadStatus { None, Loaded, Corrupt }

public sealed record StateLoadResult(StateLoadStatus Status, RunState? State, string? Error);
