using System.Text.Json.Serialization;
using FormatKit.Core.Running;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Media.Building;
using FormatKit.Media.Usb;

namespace FormatKit.App.Worker;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Planned), "planned")]
[JsonDerivedType(typeof(StepStarted), "stepStarted")]
[JsonDerivedType(typeof(StepFinished), "stepFinished")]
[JsonDerivedType(typeof(RebootCountdown), "rebootCountdown")]
[JsonDerivedType(typeof(Finished), "finished")]
[JsonDerivedType(typeof(Failed), "failed")]
[JsonDerivedType(typeof(ConsoleOpened), "consoleOpened")]
[JsonDerivedType(typeof(ConsoleLine), "consoleLine")]
[JsonDerivedType(typeof(ConsoleClosed), "consoleClosed")]
[JsonDerivedType(typeof(Cancel), "cancel")]
[JsonDerivedType(typeof(RebootNow), "rebootNow")]
[JsonDerivedType(typeof(RebootCancel), "rebootCancel")]
[JsonDerivedType(typeof(Disks), "disks")]
[JsonDerivedType(typeof(ConfirmDisk), "confirmDisk")]
[JsonDerivedType(typeof(MediaProgressUpdate), "mediaProgress")]
[JsonDerivedType(typeof(MediaFinished), "mediaFinished")]
public abstract record WorkerMessage;

public sealed record PlannedStep(string Id, string Title, bool RequiresAdmin, StepOutcome? DoneBefore = null);

/// <summary>
/// The steps the run will go through, and the profile's name for the page header (a resumed run was not reviewed in
/// this window). A missing name keeps the name the window shows.
/// </summary>
public sealed record Planned(IReadOnlyList<PlannedStep> Steps, string? ProfileName = null) : WorkerMessage;

public sealed record StepStarted(int Number, int Total, string Id) : WorkerMessage;

public sealed record StepFinished(string Id, StepOutcome Outcome, IReadOnlyList<string> Messages, string? NextAction) : WorkerMessage;

public sealed record RebootCountdown(int Seconds) : WorkerMessage;

public sealed record Finished(RunSummary Summary) : WorkerMessage;

/// <summary>
/// Why the worker sent <see cref="Failed"/>, so the window reacts to the code and never to the message text, which
/// is in the worker's UI language. Serialized as a camelCase string.
/// </summary>
public enum FailureReason
{
    [JsonStringEnumMemberName("other")] Other,

    /// <summary>The window asked to stop; the run stays saved for Resume.</summary>
    [JsonStringEnumMemberName("stopped")] Stopped,

    /// <summary>The profile's hash differs from the reviewed one; nothing ran.</summary>
    [JsonStringEnumMemberName("profileChanged")] ProfileChanged,
}

/// <summary>The run ended without a result; <paramref name="Message"/> is shown as it is. A missing reason is <see cref="FailureReason.Other"/>.</summary>
public sealed record Failed(string Message, FailureReason Reason = FailureReason.Other) : WorkerMessage;

/// <summary>A step opened a console window for the user to type in (a password or passphrase prompt).</summary>
public sealed record ConsoleOpened : WorkerMessage;

/// <summary>A line a step writes for the user (public data only, such as an SSH public key).</summary>
public sealed record ConsoleLine(string Text) : WorkerMessage;

/// <summary>The console window of <see cref="ConsoleOpened"/> has closed.</summary>
public sealed record ConsoleClosed : WorkerMessage;

public sealed record Cancel : WorkerMessage;

public sealed record RebootNow : WorkerMessage;

public sealed record RebootCancel : WorkerMessage;

/// <summary>A disk the media job can show, with the verdict of the stick rules.</summary>
public sealed record DiskRow(
    int Number,
    string UniqueId,
    string FriendlyName,
    long SizeBytes,
    string BusType,
    bool IsRemovable,
    IReadOnlyList<DiskVolume> Volumes,
    UsbRefusal Refusal,
    IReadOnlyList<string> SourcesOnDisk);

/// <summary>The USB disks; the job then waits for <see cref="ConfirmDisk"/> or <see cref="Cancel"/>.</summary>
public sealed record Disks(IReadOnlyList<DiskRow> Items, int HiddenHardDisks) : WorkerMessage;

/// <summary>
/// The disk the user selected and what they typed, unchecked: the job compares it with the disk's number, in
/// <see cref="UsbTarget.ConfirmAndPrepareAsync"/>.
/// </summary>
public sealed record ConfirmDisk(string UniqueId, string TypedNumber) : WorkerMessage;

public sealed record MediaProgressUpdate(MediaPhase Phase, long CopiedBytes, long TotalBytes) : WorkerMessage;

/// <summary>How a media job that got past its checks ended. Serialized as a camelCase string.</summary>
public enum MediaFinishedStatus
{
    [JsonStringEnumMemberName("done")] Done,

    /// <summary>The user cancelled, or typed anything but the disk's number; nothing was erased.</summary>
    [JsonStringEnumMemberName("cancelled")] Cancelled,

    /// <summary>The re-check before the erase found another disk; nothing was erased.</summary>
    [JsonStringEnumMemberName("diskChanged")] DiskChanged,

    /// <summary>The erase started, or the writing did, and did not finish: the target is partly written.</summary>
    [JsonStringEnumMemberName("incomplete")] Incomplete,
}

/// <summary>The media job's result; <paramref name="Message"/> is shown as it is, and the window acts on the status.</summary>
public sealed record MediaFinished(MediaFinishedStatus Status, string Message) : WorkerMessage;
