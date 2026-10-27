namespace FormatKit.Core.Profiles;

public enum ProfileErrorCode
{
    FileNotFound,
    InvalidJson,
    MissingField,
    InvalidValue,
    UnsupportedSchemaVersion,
    UnknownModule,
}

/// <summary>A problem found in a profile. <paramref name="Path"/> is a JSON path such as <c>modules.drivers.sources[2]</c>.</summary>
public sealed record ProfileError(ProfileErrorCode Code, string Path, string Message);
