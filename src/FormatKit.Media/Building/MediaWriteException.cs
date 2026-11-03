namespace FormatKit.Media.Building;

/// <summary>A failure while writing the media (the target folder is partly written); the CLI exits with 1.</summary>
public sealed class MediaWriteException(string message, Exception? inner = null) : Exception(message, inner);
