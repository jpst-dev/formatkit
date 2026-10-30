namespace FormatKit.Media.Building;

/// <summary>An install media validation failure (invalid ISO, edition, language or size); the CLI exits with 2.</summary>
public sealed class MediaException(string message) : Exception(message);
