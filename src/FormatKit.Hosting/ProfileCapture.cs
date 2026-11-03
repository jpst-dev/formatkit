using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FormatKit.Cleanup.Capture;
using FormatKit.Cleanup.Catalog;
using FormatKit.Core;
using FormatKit.Core.Capture;
using FormatKit.Core.Profiles;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Dev;
using FormatKit.Dev.Capture;
using FormatKit.Hosting.Localization;
using FormatKit.Media.Setup;
using FormatKit.Prepare;
using FormatKit.Programs.Capture;
using FormatKit.Programs.Catalog;

namespace FormatKit.Hosting;

/// <summary>Where each section of a captured profile comes from.</summary>
public sealed record CaptureSources(
    Func<CancellationToken, Task<CaptureSection>> Programs,
    Func<CancellationToken, Task<CaptureSection>> Cleanup,
    Func<CancellationToken, Task<CaptureSection>> Dev,
    Func<CaptureSection> Setup);

public sealed record CaptureSummary(string Section, string Text);

/// <param name="Json">The profile to write; null when it failed validation.</param>
/// <param name="Errors">Validation errors: a FormatKit bug, not something the user did.</param>
public sealed record ProfileCaptureResult(
    string? Json, IReadOnlyList<CaptureSummary> Sections, IReadOnlyList<CaptureNote> Notes, IReadOnlyList<ProfileError> Errors);

/// <param name="OnWindowsDrive">The file was written to the Windows drive, which the format erases.</param>
/// <param name="Error">The exception message when the write failed; null on success.</param>
public sealed record CaptureWrite(bool OnWindowsDrive, string? Error);

/// <summary>Assembles a profile from this machine and proves it valid before anyone relies on it after a format.</summary>
public sealed class ProfileCapture(
    IReadOnlyList<IModule> modules, IFileSystem files, IVolumes volumes, CaptureSources sources, string computerName, char systemDrive)
{
    private static readonly JsonSerializerOptions Output = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static CaptureSources RealSources(
        ServiceSet services, ProgramCatalog programs, CleanupCatalog cleanup, DevEnvironment dev,
        char systemDrive, string tempDirectory, AppPaths paths, Serilog.ILogger log) => new(
        ct => ProgramsCapture.RunAsync(services, programs, tempDirectory, log, ct),
        ct => CleanupCapture.RunAsync(services, cleanup, paths, log, ct),
        ct => DevCapture.RunAsync(services, dev, systemDrive, log, ct),
        () => SetupCapture.Run(services.Registry, dev.ComputerName));

    public async Task<ProfileCaptureResult> RunAsync(CancellationToken ct)
    {
        var drivers = new CaptureSection(
            new JsonObject { ["enabled"] = true, ["sources"] = new JsonArray("windowsUpdate", "oem", "gpu") },
            [],
            Strings.Get("Capture_DriversSummary"));
        var programs = await GuardAsync("modules.programs", sources.Programs, ct).ConfigureAwait(false);
        var cleanup = await GuardAsync("modules.cleanup", sources.Cleanup, ct).ConfigureAwait(false);
        var dev = await GuardAsync("modules.dev", sources.Dev, ct).ConfigureAwait(false);
        var setup = Guard();

        var root = new JsonObject
        {
            ["schemaVersion"] = ProfileLoader.CurrentSchemaVersion,
            ["name"] = computerName,
            ["modules"] = new JsonObject
            {
                ["drivers"] = drivers.Value,
                ["programs"] = programs.Value,
                ["cleanup"] = cleanup.Value,
                ["dev"] = dev.Value,
            },
            ["setup"] = setup.Value,
        };

        (string Name, CaptureSection Section)[] all =
            [("drivers", drivers), ("programs", programs), ("cleanup", cleanup), ("dev", dev), ("setup", setup)];
        var summaries = all.Select(s => new CaptureSummary(s.Name, s.Section.Summary)).ToList();
        var notes = all.SelectMany(s => s.Section.Notes).ToList();

        var errors = Validate(root);
        return new ProfileCaptureResult(errors.Count == 0 ? root.ToJsonString(Output) : null, summaries, notes, errors);
    }

    /// <summary>A source that fails costs only its own section; the rest of the machine is still worth keeping.</summary>
    private static async Task<CaptureSection> GuardAsync(
        string path, Func<CancellationToken, Task<CaptureSection>> source, CancellationToken ct)
    {
        try
        {
            return await source(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed(new JsonObject { ["enabled"] = false }, path, ex);
        }
    }

    private CaptureSection Guard()
    {
        try
        {
            return sources.Setup();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed([], "setup", ex);
        }
    }

    private static CaptureSection Failed(JsonObject value, string path, Exception ex)
    {
        var reason = Strings.Format("Capture_SectionFailed", ex.Message);
        return new CaptureSection(value, [new CaptureNote(CaptureNoteKind.NotCaptured, path, reason)], Strings.Get("Capture_SummaryFailed"));
    }

    /// <summary>The agreements stay false in the file: that is the user's decision, not an error in the capture.</summary>
    private IReadOnlyList<ProfileError> Validate(JsonObject root)
    {
        var copy = root.DeepClone().AsObject();
        if (copy["modules"]!["programs"] is JsonObject programs && programs.ContainsKey("acceptAgreements"))
            programs["acceptAgreements"] = true;
        var json = copy.ToJsonString();

        var loaded = new ProfileLoader(modules).Parse(json, "captured.json");
        var (_, setup) = SetupSectionParser.Parse(json);
        return [.. loaded.Errors, .. setup];
    }

    public async Task<CaptureWrite> WriteAsync(ProfileCaptureResult result, string path, CancellationToken ct)
    {
        IReadOnlyList<VolumeInfo>? known = null;
        try
        {
            known = await volumes.ListAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unknown volumes still answer for the Windows drive itself.
        }

        try
        {
            files.WriteAllTextAtomic(path, result.Json!);
            return new CaptureWrite(new WindowsVolume(systemDrive, known).IsLost(path), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new CaptureWrite(false, ex.Message);
        }
    }
}
