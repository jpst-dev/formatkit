using System.Text.Json;

namespace FormatKit.Core.Profiles;

public sealed record Profile(int SchemaVersion, string Name, IReadOnlyDictionary<string, JsonElement> Modules);

public sealed record LoadedProfile(Profile Profile, string Path, string Hash);

public sealed record ProfileLoadResult(LoadedProfile? Value, IReadOnlyList<ProfileError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
