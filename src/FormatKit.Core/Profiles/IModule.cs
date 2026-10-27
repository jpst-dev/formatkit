using System.Text.Json;
using FormatKit.Core.Steps;

namespace FormatKit.Core.Profiles;

public interface IModule
{
    string Name { get; }
    IReadOnlyList<ProfileError> Validate(JsonElement section, string path);
    bool IsEnabled(JsonElement section);
    IReadOnlyList<IStep> CreateSteps(JsonElement section);
}
