namespace FormatKit.Media.Building;

public enum MediaPhase { Copying, Splitting, WritingFormatKit, WritingAnswerFile, Done }

/// <summary>Progress of <see cref="MediaBuilder.WriteAsync"/>: the phase just started and the bytes copied so far.</summary>
public sealed record MediaProgress(MediaPhase Phase, long CopiedBytes, long TotalBytes);
