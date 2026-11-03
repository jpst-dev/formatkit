using System.Text.Json.Nodes;

namespace FormatKit.Core.Capture;

public enum CaptureNoteKind
{
    /// <summary>Something on this machine that the profile does not hold.</summary>
    NotCaptured,

    /// <summary>Something in the profile, or left out of it, that needs the user's decision.</summary>
    Review,

    /// <summary>A plain remark.</summary>
    Info,
}

/// <param name="Path">The profile path the note concerns, for example <c>modules.dev.git</c>.</param>
public sealed record CaptureNote(CaptureNoteKind Kind, string Path, string Text);

/// <summary>One section of a captured profile.</summary>
/// <param name="Summary">One localized line for the report.</param>
public sealed record CaptureSection(JsonObject Value, IReadOnlyList<CaptureNote> Notes, string Summary);
