using System.Text.Json;
using FormatKit.Core.Profiles;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Dev.Steps;

namespace FormatKit.Dev;

/// <param name="time">Stamps backups; used by the steps.</param>
/// <param name="env">The machine facts; the real ones unless a test supplies others.</param>
/// <param name="files">The file system for the steps; the real one unless a test supplies another.</param>
public sealed class DevModule(TimeProvider time, DevEnvironment? env = null, IFileSystem? files = null) : IModule
{
    // Handed to the steps as they are added.
    internal IFileSystem? Files => files;

    public string Name => "dev";

    public bool IsEnabled(JsonElement section) =>
        section.ValueKind == JsonValueKind.Object
        && section.TryGetProperty("enabled", out var enabled)
        && enabled.ValueKind == JsonValueKind.True;

    public IReadOnlyList<ProfileError> Validate(JsonElement section, string path) => Validate(section, path, null);

    /// <param name="baseDirectory">The profile file's directory; a relative dotfiles <c>path</c> resolves against it.</param>
    public IReadOnlyList<ProfileError> Validate(JsonElement section, string path, string? baseDirectory) =>
        DevOptions.Parse(section, path, baseDirectory, env ?? DevEnvironment.Current()).Errors;

    public IReadOnlyList<IStep> CreateSteps(JsonElement section) => CreateSteps(section, null);

    /// <param name="baseDirectory">The profile file's directory; a relative dotfiles <c>path</c> resolves against it.</param>
    public IReadOnlyList<IStep> CreateSteps(JsonElement section, string? baseDirectory)
    {
        if (!IsEnabled(section))
            return [];

        var (options, _) = DevOptions.Parse(section, "dev", baseDirectory, env ?? DevEnvironment.Current());
        if (options is null)
            return [];

        List<IStep> steps = [];

        // The Dev Drive step is added by the task that implements it.
        if (options.Wsl is not null)
            steps.Add(new WslStep(options.Wsl, env ?? DevEnvironment.Current(), time));
        if (options.Git is not null)
            steps.Add(new GitStep(options.Git));
        if (options.Ssh is not null)
            steps.Add(new SshStep(options.Ssh, env ?? DevEnvironment.Current()));
        if (options.Dotfiles is not null)
            steps.Add(new DotfilesStep(options.Dotfiles, env ?? DevEnvironment.Current(), time));
        if (options.Shell is not null)
            steps.Add(new ShellStep(options.Shell, options.Wsl, env ?? DevEnvironment.Current()));

        return steps;
    }
}
