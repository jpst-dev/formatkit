namespace FormatKit.Core.Profiles;

public enum ExternalInputKind { File, Folder }

/// <summary>A path a module reads at run time. <paramref name="JsonPath"/> locates it in the profile; <paramref name="ResolvedPath"/> is where the run looks.</summary>
public sealed record ExternalInput(string JsonPath, string ResolvedPath, ExternalInputKind Kind);
