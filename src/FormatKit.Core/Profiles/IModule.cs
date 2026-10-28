using System.Text.Json;
using FormatKit.Core.Steps;

namespace FormatKit.Core.Profiles;

public interface IModule
{
    string Name { get; }
    IReadOnlyList<ProfileError> Validate(JsonElement section, string path);
    bool IsEnabled(JsonElement section);
    IReadOnlyList<IStep> CreateSteps(JsonElement section);

    /// <summary>Validates with the profile file's directory, against which a module resolves relative paths.</summary>
    IReadOnlyList<ProfileError> Validate(JsonElement section, string path, string? baseDirectory) => Validate(section, path);

    /// <summary>Creates the steps with the profile file's directory, against which a module resolves relative paths.</summary>
    IReadOnlyList<IStep> CreateSteps(JsonElement section, string? baseDirectory) => CreateSteps(section);
}
