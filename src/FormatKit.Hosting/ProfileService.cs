using FormatKit.Core.Profiles;

namespace FormatKit.Hosting;

public sealed record ProfileLoadOutcome(LoadedProfile? Profile, IReadOnlyList<ProfileError> Errors);

/// <summary>Loads and validates profiles against the registered modules.</summary>
public sealed class ProfileService(IReadOnlyList<IModule> modules)
{
    public ProfileLoadOutcome Load(string path)
    {
        if (path == BuiltInProfiles.DriversOnlyPath)
        {
            return new ProfileLoadOutcome(
                new LoadedProfile(BuiltInProfiles.DriversOnly, path, BuiltInProfiles.DriversOnlyHash), []);
        }

        var result = new ProfileLoader(modules).Load(path);
        return result.IsValid
            ? new ProfileLoadOutcome(result.Value, [])
            : new ProfileLoadOutcome(null, result.Errors);
    }
}
