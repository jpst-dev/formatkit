using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FormatKit.Core;
using FormatKit.Core.Capture;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Dev.Localization;
using FormatKit.Dev.Steps;

namespace FormatKit.Dev.Capture;

/// <summary>Reads the five global Git settings a profile can hold, and no other.</summary>
internal static class GitReader
{
    private static readonly (string Key, string Property)[] Keys =
    [
        ("user.name", "name"),
        ("user.email", "email"),
        ("init.defaultBranch", "defaultBranch"),
        ("core.editor", "editor"),
        ("core.autocrlf", "autocrlf"),
    ];

    public static async Task<JsonObject?> ReadAsync(ServiceSet services, List<CaptureNote> notes, CancellationToken ct)
    {
        var git = services.Tools.Find("git.exe", KnownTools.Git);
        if (git is null)
        {
            notes.Add(new CaptureNote(CaptureNoteKind.NotCaptured, $"{DevCapture.SectionPath}.git", Strings.Get("Capture_GitMissing")));
            return null;
        }

        var part = new JsonObject();
        foreach (var (key, property) in Keys)
        {
            var result = await services.Processes
                .RunAsync(new ProcessSpec(git, ["config", "--global", "--get", key], Timeouts.Query), ct)
                .ConfigureAwait(false);
            var value = result.StdOut.Trim();
            if (result.ExitCode == 0 && value.Length > 0)
                part[property] = value;
        }

        return part.Count > 0 ? part : null;
    }
}

/// <summary>Reads the key type and comment of the public key. The private key is never opened.</summary>
internal static class SshReader
{
    public static JsonObject? Read(ServiceSet services, DevEnvironment env, List<CaptureNote> notes)
    {
        foreach (var (file, keyType) in new[] { ("id_ed25519.pub", "ed25519"), ("id_rsa.pub", "rsa") })
        {
            var path = Path.Combine(env.UserProfile, ".ssh", file);
            if (!services.Files.FileExists(path))
                continue;

            var part = new JsonObject { ["keyType"] = keyType };
            var fields = services.Files.ReadAllText(path).Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 3)
                part["comment"] = fields[2];
            notes.Add(new CaptureNote(CaptureNoteKind.Review, $"{DevCapture.SectionPath}.ssh", Strings.Get("Capture_SshKey")));
            return part;
        }

        return null;
    }
}

internal sealed record WslCapture(JsonObject? Wsl, JsonObject? Shell);

/// <summary>Reads the distro, its default user and its shell, read-only, plus <c>.wslconfig</c>.</summary>
internal static partial class WslReader
{
    private const string ShellScript =
        "command -v zsh >/dev/null 2>&1 && echo zsh=1; " +
        "[ -d \"$HOME/.oh-my-zsh\" ] && echo omz=1; " +
        "if [ -f \"$HOME/.zshrc\" ]; then grep -E '^ZSH_THEME=' \"$HOME/.zshrc\"; sed -n '/^plugins=(/,/)/p' \"$HOME/.zshrc\"; fi; " +
        "exit 0";

    [GeneratedRegex("""^ZSH_THEME=["']?([^"'\s]*)""", RegexOptions.Multiline)]
    private static partial Regex ThemeLine();

    [GeneratedRegex(@"^plugins=\(([^)]*)\)", RegexOptions.Multiline)]
    private static partial Regex PluginsLine();

    public static async Task<WslCapture?> ReadAsync(ServiceSet services, DevEnvironment env, List<CaptureNote> notes, CancellationToken ct)
    {
        IReadOnlyList<string> installed;
        try
        {
            installed = await services.Wsl.ListDistrosAsync(ct).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        var distro = DevOptions.Distros.FirstOrDefault(d => installed.Contains(d, StringComparer.OrdinalIgnoreCase));
        var others = installed.Where(d => !string.Equals(d, distro, StringComparison.OrdinalIgnoreCase)).ToList();
        var path = $"{DevCapture.SectionPath}.wsl";
        if (others.Count > 0)
            notes.Add(new CaptureNote(CaptureNoteKind.NotCaptured, path, Strings.Format("Capture_OtherDistros", string.Join(", ", others))));
        if (distro is null)
            return null;

        var whoami = await services.Wsl.ExecAsDefaultUserAsync(distro, ["whoami"], ct).ConfigureAwait(false);
        var user = whoami.StdOut.Trim();
        if (whoami.ExitCode != 0 || user.Length == 0)
        {
            notes.Add(new CaptureNote(CaptureNoteKind.NotCaptured, path, Strings.Format("Capture_WslFailed", distro, whoami.ExitCode)));
            return null;
        }

        var wsl = new JsonObject { ["distro"] = distro, ["user"] = user };
        if (ReadConfig(services, env) is { } config)
            wsl["wslconfig"] = config;

        var shell = await services.Wsl.ExecAsDefaultUserAsync(distro, ["sh", "-c", ShellScript], ct).ConfigureAwait(false);
        if (shell.ExitCode != 0)
        {
            notes.Add(new CaptureNote(CaptureNoteKind.NotCaptured, $"{DevCapture.SectionPath}.shell.wsl", Strings.Format("Capture_WslFailed", distro, shell.ExitCode)));
            return new WslCapture(wsl, null);
        }

        return new WslCapture(wsl, ParseShell(shell.StdOut));
    }

    private static JsonObject? ParseShell(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.TrimEntries);
        if (!lines.Contains("zsh=1"))
            return null;

        var part = new JsonObject { ["zsh"] = true, ["ohMyZsh"] = lines.Contains("omz=1") };
        var normalized = output.Replace("\r", "");
        var theme = ThemeLine().Match(normalized);
        if (theme.Success && theme.Groups[1].Length > 0)
            part["theme"] = theme.Groups[1].Value;

        var plugins = PluginsLine().Match(normalized);
        if (plugins.Success)
        {
            var names = plugins.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            part["plugins"] = new JsonArray(names.Select(n => (JsonNode)JsonValue.Create(n)).ToArray());
        }

        return part;
    }

    /// <summary>The three settings a profile can hold from the <c>[wsl2]</c> section; the parser judges their values.</summary>
    private static JsonObject? ReadConfig(ServiceSet services, DevEnvironment env)
    {
        var path = Path.Combine(env.UserProfile, ".wslconfig");
        if (!services.Files.FileExists(path))
            return null;

        var config = new JsonObject();
        var inSection = false;
        foreach (var raw in services.Files.ReadAllText(path).Split('\n', StringSplitOptions.TrimEntries))
        {
            if (raw.StartsWith('['))
            {
                inSection = raw.Equals("[wsl2]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var equals = raw.IndexOf('=');
            if (!inSection || equals <= 0 || raw.StartsWith('#') || raw.StartsWith(';'))
                continue;

            var key = raw[..equals].Trim().ToLowerInvariant();
            var value = raw[(equals + 1)..].Trim();
            if (key is "memory" or "swap")
                config[key] = value;
            else if (key == "processors")
                config[key] = int.TryParse(value, out var n) ? JsonValue.Create(n) : JsonValue.Create(value);
        }

        return config.Count > 0 ? config : null;
    }
}

/// <summary>Reads Oh My Posh from the PowerShell profiles, and the first known Nerd Font installed.</summary>
internal static partial class WindowsShellReader
{
    [GeneratedRegex(@"oh-my-posh(?:\.exe)?['""]?\s+init\s+\S+(?<rest>.*)", RegexOptions.IgnoreCase)]
    private static partial Regex InitLine();

    [GeneratedRegex(@"--config[=\s]\s*['""]?(?<config>[^'""|\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ConfigOption();

    [GeneratedRegex(@"\.omp\.(json|ya?ml|toml)\z", RegexOptions.IgnoreCase)]
    private static partial Regex ThemeExtension();

    public static JsonObject? Read(ServiceSet services, DevEnvironment env, Serilog.ILogger log, List<CaptureNote> notes)
    {
        if (services.Tools.Find("oh-my-posh.exe", KnownTools.OhMyPosh) is null)
            return null;

        var rest = FindInit(services, env);
        if (rest is null)
            return null;

        var part = new JsonObject { ["ohMyPosh"] = true };
        var config = ConfigOption().Match(rest);
        if (config.Success)
        {
            var value = config.Groups["config"].Value;
            var theme = ThemeExtension().Replace(value[(value.LastIndexOfAny(['/', '\\']) + 1)..], "");
            part["theme"] = theme;
            if (theme != value)
                notes.Add(new CaptureNote(CaptureNoteKind.Review, $"{DevCapture.SectionPath}.shell.windows.theme", Strings.Format("Capture_ThemeReduced", value, theme)));
        }

        var context = new StepContext(services, log);
        var font = TerminalFragment.FontFamilies.Keys.FirstOrDefault(f => ShellStep.IsFontInstalled(f, context));
        if (font is not null)
            part["font"] = font;
        return part;
    }

    private static string? FindInit(ServiceSet services, DevEnvironment env)
    {
        foreach (var folder in new[] { "PowerShell", "WindowsPowerShell" })
        {
            var path = Path.Combine(env.Documents, folder, "Microsoft.PowerShell_profile.ps1");
            if (!services.Files.FileExists(path))
                continue;

            foreach (var line in services.Files.ReadAllText(path).Split('\n', StringSplitOptions.TrimEntries))
            {
                var match = line.StartsWith('#') ? Match.Empty : InitLine().Match(line);
                if (match.Success)
                    return match.Groups["rest"].Value;
            }
        }

        return null;
    }
}

/// <summary>Reads the Dev Drive this machine has attached, when it would survive the format.</summary>
internal static class DevDriveReader
{
    private const long BytesPerGB = 1024L * 1024 * 1024;

    public static async Task<JsonObject?> ReadAsync(ServiceSet services, char systemDrive, List<CaptureNote> notes, CancellationToken ct)
    {
        var path = $"{DevCapture.SectionPath}.devDrive";
        var volumes = (await services.Volumes.ListAsync(ct).ConfigureAwait(false))
            .Where(v => !string.IsNullOrEmpty(v.BackingVhdx))
            .ToList();
        if (volumes.Count == 0)
            return null;

        var volume = volumes[0];
        if (volumes.Count > 1)
            notes.Add(new CaptureNote(CaptureNoteKind.NotCaptured, path, Strings.Format("Capture_OtherDrives", string.Join(", ", volumes.Skip(1).Select(v => v.BackingVhdx)))));

        var vhdx = volume.BackingVhdx!;
        if (char.ToUpperInvariant(vhdx[0]) == char.ToUpperInvariant(systemDrive))
        {
            notes.Add(new CaptureNote(CaptureNoteKind.NotCaptured, path, Strings.Format("Capture_DriveOnWindows", vhdx)));
            return null;
        }

        var state = await services.VirtualDisks.GetAsync(vhdx, ct).ConfigureAwait(false);
        if (state?.SizeBytes is not { } size)
        {
            notes.Add(new CaptureNote(CaptureNoteKind.NotCaptured, path, Strings.Format("Capture_DriveSizeUnknown", vhdx)));
            return null;
        }

        var part = new JsonObject
        {
            ["path"] = vhdx,
            ["sizeGB"] = (int)(size / BytesPerGB),
            ["letter"] = volume.Letter.ToString(),
        };
        if (!string.IsNullOrEmpty(state.Label))
            part["label"] = state.Label;

        var prefix = $"{volume.Letter}:\\";
        var caches = DevDriveStep.CacheVariables
            .Where(c => services.UserEnvironment.Get(c.Value)?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true)
            .Select(c => (JsonNode)JsonValue.Create(c.Key))
            .ToArray();
        if (caches.Length > 0)
            part["caches"] = new JsonArray(caches);
        return part;
    }
}
