using System.Text.Json;

namespace FormatKit.Core.Profiles;

/// <summary><paramref name="BaseDirectory"/> is the directory of the profile file; relative paths in sections resolve against it.</summary>
public sealed record Profile(int SchemaVersion, string Name, IReadOnlyDictionary<string, JsonElement> Modules, string? BaseDirectory = null);

public sealed record LoadedProfile(Profile Profile, string Path, string Hash);

public sealed record ProfileLoadResult(LoadedProfile? Value, IReadOnlyList<ProfileError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
