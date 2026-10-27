using System.Text.Json;
using FormatKit.Core.Profiles;
using FormatKit.Core.Steps;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeModule(string name) : IModule
{
    public string Name { get; } = name;
    public List<ProfileError> Errors { get; } = [];
    public List<IStep> Steps { get; } = [];
    public List<(JsonElement Section, string Path)> ValidateCalls { get; } = [];

    public IReadOnlyList<ProfileError> Validate(JsonElement section, string path)
    {
        ValidateCalls.Add((section, path));
        return Errors;
    }

    public bool IsEnabled(JsonElement section) =>
        section.ValueKind == JsonValueKind.Object
        && section.TryGetProperty("enabled", out var e)
        && e.ValueKind == JsonValueKind.True;

    public IReadOnlyList<IStep> CreateSteps(JsonElement section) => Steps;
}
