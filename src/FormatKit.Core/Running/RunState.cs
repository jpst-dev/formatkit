using System.Text.Json.Serialization;
using FormatKit.Core.Steps;

namespace FormatKit.Core.Running;

public sealed record StepRecord(
    [property: JsonRequired] StepOutcome Outcome,
    DateTimeOffset FinishedAt,
    IReadOnlyList<string> Messages,
    string? NextAction)
{
    /// <summary>The step asked to be executed again after its reboot.</summary>
    public bool RunAgain { get; init; }
}

public sealed record RunState(
    string ProfilePath,
    string ProfileHash,
    IReadOnlyList<string> PlanStepIds,
    Dictionary<string, StepRecord> Results,
    DateTimeOffset StartedAt)
{
    /// <summary>Steps already executed again after a reboot; a second request is treated as a plain reboot, so reboots cannot loop.</summary>
    public List<string> RanAgain { get; init; } = [];
}

public enum StateLoadStatus { None, Loaded, Corrupt }

public sealed record StateLoadResult(StateLoadStatus Status, RunState? State, string? Error);
