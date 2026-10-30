using System.Security;
using FormatKit.Core.Profiles;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Prepare;
using FormatKit.Prepare.Rendering;

namespace FormatKit.Hosting;

public sealed record PrepareRun(PrepareReport Report, WindowsVolume Windows, string ComputerName);

/// <param name="OnWindowsDrive">The file was written to the Windows drive, which the format erases.</param>
/// <param name="Error">The exception message when the write failed; null on success.</param>
public sealed record MarkdownWrite(bool OnWindowsDrive, string? Error);

/// <summary>The preparation checklist run shared by the CLI and the app.</summary>
public sealed class PrepareRunner(
    HostEnvironment env,
    IReadOnlyList<IModule> modules,
    Func<IReadOnlyList<IPrepareCheck>> checks,
    Func<PrepareEnvironment> environment)
{
    public async Task<PrepareRun> RunAsync(LoadedProfile? profile, CancellationToken ct)
    {
        var machine = environment();
        var volumes = await ListVolumesAsync(ct);
        var windows = new WindowsVolume(machine.SystemDrive, volumes);
        var context = new PrepareContext(
            env.Services, machine, windows, profile, CollectInputs(profile), env.Elevation.IsElevated, env.Log);

        var report = await new PrepareChecklist(checks()).RunAsync(context, ct);
        return new PrepareRun(report, windows, machine.ComputerName);
    }

    public MarkdownWrite WriteMarkdown(PrepareRun run, string path)
    {
        var onWindowsDrive = false;
        try
        {
            // IsLost resolves the path, so an invalid one fails here and is reported like a failed write.
            onWindowsDrive = run.Windows.IsLost(path);
            env.Services.Files.WriteAllTextAtomic(
                path, MarkdownRenderer.Render(run.Report, run.ComputerName, TimeProvider.System.GetLocalNow()));
            return new MarkdownWrite(onWindowsDrive, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or ArgumentException or SecurityException)
        {
            env.Log.Information(ex, "Could not write the checklist to {Path}", path);
            return new MarkdownWrite(onWindowsDrive, ex.Message);
        }
    }

    /// <summary>Read once per run and shared through the context; null when listing failed (other drives are then unknown).</summary>
    private async Task<IReadOnlyList<VolumeInfo>?> ListVolumesAsync(CancellationToken ct)
    {
        try
        {
            return await env.Services.Volumes.ListAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            env.Log.Information(ex, "Could not list the volumes; the volume checks report them as not checked");
            return null;
        }
    }

    /// <summary>Inputs of every enabled module (the profile is already valid), plus the profile file itself.</summary>
    private List<ExternalInput> CollectInputs(LoadedProfile? profile)
    {
        var inputs = new List<ExternalInput>();
        if (profile is null)
            return inputs;

        foreach (var module in modules)
        {
            if (profile.Profile.Modules.TryGetValue(module.Name, out var section) && module.IsEnabled(section))
                inputs.AddRange(module.ExternalInputs(section, profile.Profile.BaseDirectory));
        }

        inputs.Add(new ExternalInput("", profile.Path, ExternalInputKind.File));
        return inputs;
    }
}
