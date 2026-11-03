using System.Text.Json.Nodes;
using FormatKit.Core.Capture;
using FormatKit.Core.Steps;
using FormatKit.Dev.Localization;

namespace FormatKit.Dev.Capture;

/// <summary>Captures the dev environment settings of this machine. Each part is read on its own, so one failure costs only that part.</summary>
public static class DevCapture
{
    internal const string SectionPath = "modules.dev";

    public static async Task<CaptureSection> RunAsync(
        ServiceSet services, DevEnvironment env, char systemDrive, Serilog.ILogger log, CancellationToken ct)
    {
        var notes = new List<CaptureNote>();

        async Task<T?> Guard<T>(string part, Func<Task<T?>> read) where T : class
        {
            try
            {
                return await read().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.Warning(ex, "Capturing {Part} failed", part);
                notes.Add(new CaptureNote(CaptureNoteKind.NotCaptured, $"{SectionPath}.{part}", Strings.Format("Capture_PartFailed", ex.Message)));
                return null;
            }
        }

        var git = await Guard("git", () => GitReader.ReadAsync(services, notes, ct));
        var ssh = await Guard("ssh", () => Task.FromResult(SshReader.Read(services, env, notes)));
        var wsl = await Guard("wsl", () => WslReader.ReadAsync(services, env, notes, ct));
        var windows = await Guard("shell.windows", () => Task.FromResult(WindowsShellReader.Read(services, env, log, notes)));
        var drive = await Guard("devDrive", () => DevDriveReader.ReadAsync(services, systemDrive, notes, ct));

        var value = new JsonObject { ["enabled"] = false };
        if (git is not null)
            value["git"] = git;
        if (ssh is not null)
            value["ssh"] = ssh;
        if (wsl?.Wsl is not null)
            value["wsl"] = wsl.Wsl;
        if (wsl?.Shell is not null || windows is not null)
        {
            var shell = new JsonObject();
            if (wsl?.Shell is not null)
                shell["wsl"] = wsl.Shell;
            if (windows is not null)
                shell["windows"] = windows;
            value["shell"] = shell;
        }

        if (drive is not null)
            value["devDrive"] = drive;

        CaptureCleaner.Clean(value, env, notes);

        var parts = value.Select(p => p.Key).Where(k => k != "enabled").ToList();
        value["enabled"] = parts.Count > 0;
        notes.Add(new CaptureNote(CaptureNoteKind.NotCaptured, $"{SectionPath}.dotfiles", Strings.Get("Capture_Dotfiles")));

        var summary = parts.Count > 0 ? string.Join(", ", parts) : Strings.Get("Capture_SummaryNone");
        return new CaptureSection(value, notes, summary);
    }
}
