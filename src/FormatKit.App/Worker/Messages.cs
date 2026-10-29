using System.Text.Json.Serialization;
using FormatKit.Core.Running;
using FormatKit.Core.Steps;

namespace FormatKit.App.Worker;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Planned), "planned")]
[JsonDerivedType(typeof(StepStarted), "stepStarted")]
[JsonDerivedType(typeof(StepFinished), "stepFinished")]
[JsonDerivedType(typeof(RebootCountdown), "rebootCountdown")]
[JsonDerivedType(typeof(Finished), "finished")]
[JsonDerivedType(typeof(Failed), "failed")]
[JsonDerivedType(typeof(Cancel), "cancel")]
[JsonDerivedType(typeof(RebootNow), "rebootNow")]
[JsonDerivedType(typeof(RebootCancel), "rebootCancel")]
public abstract record WorkerMessage;

public sealed record PlannedStep(string Id, string Title, bool RequiresAdmin);

public sealed record Planned(IReadOnlyList<PlannedStep> Steps) : WorkerMessage;

public sealed record StepStarted(int Number, int Total, string Id) : WorkerMessage;

public sealed record StepFinished(string Id, StepOutcome Outcome, IReadOnlyList<string> Messages, string? NextAction) : WorkerMessage;

public sealed record RebootCountdown(int Seconds) : WorkerMessage;

public sealed record Finished(RunSummary Summary) : WorkerMessage;

public sealed record Failed(string Message) : WorkerMessage;

public sealed record Cancel : WorkerMessage;

public sealed record RebootNow : WorkerMessage;

public sealed record RebootCancel : WorkerMessage;
