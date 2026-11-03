using FormatKit.Core.Profiles;
using FormatKit.Core.Steps;
using FormatKit.Media.Building;
using FormatKit.Media.Bundle;
using FormatKit.Media.Setup;
using Serilog;

namespace FormatKit.Hosting;

/// <summary>
/// The result of <see cref="MediaRunner.PrepareAsync"/>. <see cref="Media"/> is set when everything checked out, and
/// the caller then owns (and disposes) the mounted ISO; otherwise the other members say why, for the caller to show.
/// </summary>
public sealed record MediaPrepareOutcome(
    MediaRequest? Request = null,
    PreparedMedia? Media = null,
    IReadOnlyList<ProfileError>? Errors = null,
    bool ProfileChanged = false,
    IReadOnlyList<string>? Missing = null,
    IReadOnlyList<string>? Unreadable = null,
    string? Error = null)
{
    public IReadOnlyList<ProfileError> ProfileErrors => Errors ?? [];
    public IReadOnlyList<string> MissingInputs => Missing ?? [];
    public IReadOnlyList<string> UnreadableInputs => Unreadable ?? [];
}

/// <summary>What <c>media build</c>, <c>media usb</c> and the UI's media job do before writing anything.</summary>
public sealed class MediaRunner(ServiceSet services, ILogger log, IReadOnlyList<IModule> modules)
{
    /// <summary>
    /// Loads and checks the profile and its inputs, then mounts and checks the ISO. With <paramref name="expectedHash"/>,
    /// a profile with another hash is refused before anything is mounted. <paramref name="exePath"/> is the
    /// <c>formatkit.exe</c> to bundle.
    /// </summary>
    public async Task<MediaPrepareOutcome> PrepareAsync(string iso, string? profilePath, string? expectedHash,
        MachineLocale machine, string exePath, CancellationToken ct)
    {
        var setup = new SetupOptions();
        ProfileBundle? bundle = null;

        if (profilePath is not null)
        {
            var loaded = new ProfileService(modules).Load(profilePath);
            if (loaded.Profile is not { } profile)
                return new MediaPrepareOutcome(Errors: loaded.Errors);
            if (expectedHash is not null && !string.Equals(profile.Hash, expectedHash, StringComparison.Ordinal))
                return new MediaPrepareOutcome(ProfileChanged: true);

            var (options, errors) = SetupSectionParser.Parse(File.ReadAllText(profile.Path));
            if (options is null)
                return new MediaPrepareOutcome(Errors: errors);

            setup = options;
            bundle = ProfileBundle.Plan(profile, CollectInputs(profile), services.Files);
            if (bundle.Missing.Count > 0 || bundle.Unreadable.Count > 0)
                return new MediaPrepareOutcome(Missing: bundle.Missing, Unreadable: bundle.Unreadable);
        }

        var request = new MediaRequest(iso, setup, bundle, machine, exePath);
        try
        {
            return new MediaPrepareOutcome(request, await new MediaBuilder(services, log).PrepareAsync(request, ct));
        }
        catch (MediaException ex)
        {
            return new MediaPrepareOutcome(Error: ex.Message);
        }
    }

    /// <summary>Every path the media reads from, and <paramref name="extra"/>: the disk that holds one must not be erased.</summary>
    public static IReadOnlyList<string> SourcePaths(MediaRequest request, string? profilePath, params string[] extra)
    {
        var sources = new List<string> { request.IsoPath, request.ExePath };
        if (profilePath is not null)
            sources.Add(profilePath);
        if (request.Bundle is { } bundle)
            sources.AddRange(bundle.Items.Select(i => i.Input.ResolvedPath));
        sources.AddRange(extra);
        return sources;
    }

    /// <summary>Inputs of every enabled module; the bundle writes the profile itself.</summary>
    private List<ExternalInput> CollectInputs(LoadedProfile profile)
    {
        var inputs = new List<ExternalInput>();
        foreach (var module in modules)
        {
            if (profile.Profile.Modules.TryGetValue(module.Name, out var section) && module.IsEnabled(section))
                inputs.AddRange(module.ExternalInputs(section, profile.Profile.BaseDirectory));
        }

        return inputs;
    }
}
