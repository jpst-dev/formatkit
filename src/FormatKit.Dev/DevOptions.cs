using System.Text.Json;
using System.Text.RegularExpressions;
using FormatKit.Core.Profiles;
using FormatKit.Dev.Localization;

namespace FormatKit.Dev;

public sealed record WslConfig(string? Memory, int? Processors, string? Swap);

public sealed record WslOptions(string Distro, string User, WslConfig? Config);

public sealed record GitOptions(string? Name, string? Email, string? DefaultBranch, string? Editor, string? AutoCrlf);

public sealed record SshOptions(string KeyType, string Comment);

/// <summary><paramref name="To"/> is expanded and absolute, inside the user profile.</summary>
public sealed record DotfileEntry(string From, string To);

/// <summary>Exactly one of <paramref name="Repo"/> and <paramref name="LocalPath"/> is set.</summary>
public sealed record DotfilesOptions(string? Repo, string? LocalPath, string CloneTo, IReadOnlyList<DotfileEntry> Files);

public sealed record WslShellOptions(bool Zsh, bool OhMyZsh, string Theme, IReadOnlyList<string> Plugins);

public sealed record WindowsShellOptions(bool OhMyPosh, string Theme, string Font);

public sealed record ShellOptions(WslShellOptions? Wsl, WindowsShellOptions? Windows);

public sealed record DevDriveOptions(string Path, int SizeGB, char Letter, string Label, IReadOnlyList<string> Caches);

/// <summary>A null subsection means the profile has none, so it adds no step.</summary>
public sealed partial record DevOptions(
    WslOptions? Wsl, GitOptions? Git, SshOptions? Ssh, DotfilesOptions? Dotfiles, ShellOptions? Shell, DevDriveOptions? DevDrive)
{
    private static readonly string[] Distros = ["Ubuntu", "Ubuntu-24.04", "Ubuntu-22.04", "Debian"];
    private static readonly string[] ExternalPlugins = ["zsh-autosuggestions", "zsh-syntax-highlighting"];
    private static readonly string[] Fonts = ["Meslo", "CascadiaCode", "FiraCode", "JetBrainsMono", "Hack"];
    private static readonly string[] CacheNames = ["npm", "nuget", "pip", "cargo"];

    [GeneratedRegex(@"\A[a-z_][a-z0-9_-]{0,31}\z")]
    private static partial Regex UserPattern();

    [GeneratedRegex(@"\A[0-9]+(MB|GB)\z")]
    private static partial Regex MemoryPattern();

    [GeneratedRegex(@"\A[A-Za-z0-9._/-]+\z")]
    private static partial Regex BranchPattern();

    [GeneratedRegex(@"\A[A-Za-z0-9._@+-]{1,100}\z")]
    private static partial Regex CommentPattern();

    [GeneratedRegex(@"\A(https://\S+|git@[A-Za-z0-9.-]+:\S+)\z")]
    private static partial Regex RepoPattern();

    [GeneratedRegex(@"\A[A-Za-z0-9._-]+\z")]
    private static partial Regex ThemePattern();

    [GeneratedRegex(@"\A[A-Za-z0-9 _-]{1,32}\z")]
    private static partial Regex LabelPattern();

    /// <summary>
    /// Parses and validates the section; <c>Options</c> is null whenever <c>Errors</c> is not empty.
    /// A relative dotfiles <c>path</c> resolves against <paramref name="baseDirectory"/> (the profile file's directory) when given.
    /// </summary>
    public static (DevOptions? Options, IReadOnlyList<ProfileError> Errors) Parse(
        JsonElement section, string path, string? baseDirectory, DevEnvironment env)
    {
        if (section.ValueKind != JsonValueKind.Object)
            return (null, [Invalid(path, "object")]);

        var errors = new List<ProfileError>();
        CheckProperties(section, path, ["enabled", "wsl", "git", "ssh", "dotfiles", "shell", "devDrive"], errors);

        if (section.TryGetProperty("enabled", out var enabled) && enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            errors.Add(Invalid($"{path}.enabled", "boolean"));

        var wsl = ReadWsl(section, path, env, errors);
        var git = ReadGit(section, path, errors);
        var ssh = ReadSsh(section, path, env, errors);
        var dotfiles = ReadDotfiles(section, path, baseDirectory, env, errors);
        var shell = ReadShell(section, path, section.TryGetProperty("wsl", out _), errors);
        var devDrive = ReadDevDrive(section, path, errors);

        if (errors.Count > 0)
            return (null, errors);

        return (new DevOptions(wsl, git, ssh, dotfiles, shell, devDrive), errors);
    }

    private static WslOptions? ReadWsl(JsonElement section, string parent, DevEnvironment env, List<ProfileError> errors)
    {
        if (!TryObject(section, "wsl", parent, errors, out var el, out var p))
            return null;
        CheckProperties(el, p, ["distro", "user", "wslconfig"], errors);

        var distro = ReadString(el, "distro", p, errors) ?? "Ubuntu";
        if (!Distros.Contains(distro, StringComparer.Ordinal))
            errors.Add(Invalid($"{p}.distro", string.Join(" | ", Distros)));

        var user = ReadString(el, "user", p, errors, required: true);
        if (user == "root")
            errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{p}.user", Strings.Get("Validation_RootUser")));
        else if (user is not null && !UserPattern().IsMatch(user))
            errors.Add(Pattern($"{p}.user", UserPattern()));

        WslConfig? config = null;
        if (TryObject(el, "wslconfig", p, errors, out var cfg, out var cp))
        {
            CheckProperties(cfg, cp, ["memory", "processors", "swap"], errors);
            var memory = ReadMatching(cfg, "memory", cp, MemoryPattern(), errors);
            var swap = ReadMatching(cfg, "swap", cp, MemoryPattern(), errors);

            int? processors = null;
            if (cfg.TryGetProperty("processors", out var proc))
            {
                if (proc.ValueKind != JsonValueKind.Number || !proc.TryGetInt32(out var n))
                    errors.Add(Invalid($"{cp}.processors", "integer"));
                else if (n < 1 || n > env.LogicalProcessors)
                    errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{cp}.processors",
                        Strings.Format("Validation_Processors", env.LogicalProcessors)));
                else
                    processors = n;
            }

            config = new WslConfig(memory, processors, swap);
        }

        return new WslOptions(distro, user ?? "", config);
    }

    private static GitOptions? ReadGit(JsonElement section, string parent, List<ProfileError> errors)
    {
        if (!TryObject(section, "git", parent, errors, out var el, out var p))
            return null;
        string[] known = ["name", "email", "defaultBranch", "editor", "autocrlf"];
        CheckProperties(el, p, known, errors);

        if (!known.Any(k => el.TryGetProperty(k, out _)))
            errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, p, Strings.Get("Validation_GitNeedsKey")));

        var name = ReadNoControl(el, "name", p, errors);
        var email = ReadNoControl(el, "email", p, errors);
        if (email is not null && !email.Contains('@'))
            errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{p}.email", Strings.Get("Validation_EmailAt")));
        var branch = ReadMatching(el, "defaultBranch", p, BranchPattern(), errors);
        var editor = ReadNoControl(el, "editor", p, errors);

        var autoCrlf = ReadString(el, "autocrlf", p, errors);
        if (autoCrlf is not null && autoCrlf is not ("true" or "false" or "input"))
        {
            errors.Add(Invalid($"{p}.autocrlf", "true | false | input"));
            autoCrlf = null;
        }

        return new GitOptions(name, email, branch, editor, autoCrlf);
    }

    private static SshOptions? ReadSsh(JsonElement section, string parent, DevEnvironment env, List<ProfileError> errors)
    {
        if (!TryObject(section, "ssh", parent, errors, out var el, out var p))
            return null;
        CheckProperties(el, p, ["keyType", "comment"], errors);

        var keyType = ReadString(el, "keyType", p, errors) ?? "ed25519";
        if (keyType is not ("ed25519" or "rsa"))
            errors.Add(Invalid($"{p}.keyType", "ed25519 | rsa"));

        var comment = ReadMatching(el, "comment", p, CommentPattern(), errors)
            ?? $"{env.UserName}@{env.ComputerName}".ToLowerInvariant();

        return new SshOptions(keyType, comment);
    }

    private static DotfilesOptions? ReadDotfiles(
        JsonElement section, string parent, string? baseDirectory, DevEnvironment env, List<ProfileError> errors)
    {
        if (!TryObject(section, "dotfiles", parent, errors, out var el, out var p))
            return null;
        CheckProperties(el, p, ["repo", "path", "cloneTo", "files"], errors);

        var repo = ReadString(el, "repo", p, errors);
        var localPath = ReadString(el, "path", p, errors);
        if (el.TryGetProperty("repo", out _) == el.TryGetProperty("path", out _))
            errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, p, Strings.Get("Validation_RepoOrPath")));
        if (repo is not null && !RepoPattern().IsMatch(repo))
            errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{p}.repo", Strings.Get("Validation_RepoScheme")));
        if (localPath is not null)
            localPath = Resolve(env.Expand(localPath), baseDirectory);

        var cloneTo = env.Expand(ReadString(el, "cloneTo", p, errors) ?? @"%USERPROFILE%\dotfiles");

        var files = new List<DotfileEntry>();
        if (!el.TryGetProperty("files", out var list))
        {
            errors.Add(Missing($"{p}.files", "files"));
        }
        else if (list.ValueKind != JsonValueKind.Array)
        {
            errors.Add(Invalid($"{p}.files", "array"));
        }
        else
        {
            var index = 0;
            foreach (var item in list.EnumerateArray())
            {
                var fp = $"{p}.files[{index++}]";
                if (item.ValueKind != JsonValueKind.Object)
                {
                    errors.Add(Invalid(fp, "object"));
                    continue;
                }

                CheckProperties(item, fp, ["from", "to"], errors);
                var from = ReadString(item, "from", fp, errors, required: true);
                var to = ReadString(item, "to", fp, errors, required: true);

                var ok = true;
                if (from is not null && (System.IO.Path.IsPathRooted(from) || from.Split('/', '\\').Contains("..")))
                {
                    errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{fp}.from", Strings.Get("Validation_FromRelative")));
                    ok = false;
                }

                if (to is not null)
                {
                    to = env.Expand(to);
                    if (!IsInside(to, env.UserProfile))
                    {
                        errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{fp}.to",
                            Strings.Format("Validation_ToInsideProfile", env.UserProfile)));
                        ok = false;
                    }
                }

                if (ok && from is not null && to is not null)
                    files.Add(new DotfileEntry(from, to));
            }

            if (index == 0)
                errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{p}.files", Strings.Get("Validation_NeedsFile")));
        }

        return new DotfilesOptions(repo, localPath, cloneTo, files);
    }

    private static ShellOptions? ReadShell(JsonElement section, string parent, bool hasWsl, List<ProfileError> errors)
    {
        if (!TryObject(section, "shell", parent, errors, out var el, out var p))
            return null;
        CheckProperties(el, p, ["wsl", "windows"], errors);

        WslShellOptions? wsl = null;
        if (TryObject(el, "wsl", p, errors, out var w, out var wp))
        {
            CheckProperties(w, wp, ["zsh", "ohMyZsh", "theme", "plugins"], errors);
            if (!hasWsl)
                errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, wp, Strings.Get("Validation_ShellWslNeedsWsl")));

            var zsh = ReadBool(w, "zsh", wp, errors);
            var ohMyZsh = ReadBool(w, "ohMyZsh", wp, errors);
            if (ohMyZsh && !zsh)
                errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{wp}.ohMyZsh", Strings.Get("Validation_OhMyZshNeedsZsh")));

            var theme = ReadMatching(w, "theme", wp, ThemePattern(), errors) ?? "robbyrussell";

            var plugins = new List<string>();
            if (!w.TryGetProperty("plugins", out var list))
            {
                plugins.Add("git");
            }
            else if (list.ValueKind != JsonValueKind.Array)
            {
                errors.Add(Invalid($"{wp}.plugins", "array"));
            }
            else
            {
                var index = 0;
                foreach (var item in list.EnumerateArray())
                {
                    var ip = $"{wp}.plugins[{index++}]";
                    if (item.ValueKind != JsonValueKind.String)
                        errors.Add(Invalid(ip, "string"));
                    else if (!ThemePattern().IsMatch(item.GetString()!))
                        errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, ip,
                            Strings.Format("Validation_Plugin", string.Join(", ", ExternalPlugins))));
                    else
                        plugins.Add(item.GetString()!);
                }
            }

            wsl = new WslShellOptions(zsh, ohMyZsh, theme, plugins);
        }

        WindowsShellOptions? windows = null;
        if (TryObject(el, "windows", p, errors, out var win, out var winp))
        {
            CheckProperties(win, winp, ["ohMyPosh", "theme", "font"], errors);
            var ohMyPosh = ReadBool(win, "ohMyPosh", winp, errors);
            var theme = ReadMatching(win, "theme", winp, ThemePattern(), errors) ?? "paradox";

            var font = ReadString(win, "font", winp, errors) ?? "Meslo";
            if (!Fonts.Contains(font, StringComparer.Ordinal))
                errors.Add(Invalid($"{winp}.font", string.Join(" | ", Fonts)));

            windows = new WindowsShellOptions(ohMyPosh, theme, font);
        }

        return new ShellOptions(wsl, windows);
    }

    private static DevDriveOptions? ReadDevDrive(JsonElement section, string parent, List<ProfileError> errors)
    {
        if (!TryObject(section, "devDrive", parent, errors, out var el, out var p))
            return null;
        CheckProperties(el, p, ["path", "sizeGB", "letter", "label", "caches"], errors);

        var drivePath = ReadString(el, "path", p, errors, required: true);
        if (drivePath is not null && !IsDriveLetterPath(drivePath, ".vhdx"))
            errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{p}.path", Strings.Get("Validation_VhdxPath")));

        var size = 0;
        if (!el.TryGetProperty("sizeGB", out var sizeEl))
            errors.Add(Missing($"{p}.sizeGB", "sizeGB"));
        else if (sizeEl.ValueKind != JsonValueKind.Number || !sizeEl.TryGetInt32(out size))
            errors.Add(Invalid($"{p}.sizeGB", "integer"));
        else if (size < 50)
            errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{p}.sizeGB", Strings.Get("Validation_SizeMin")));

        var letter = 'D';
        var letterText = ReadString(el, "letter", p, errors, required: true);
        if (letterText is not null)
        {
            if (letterText.Length == 1 && char.IsAsciiLetter(letterText[0]) && char.ToUpperInvariant(letterText[0]) != 'C')
                letter = char.ToUpperInvariant(letterText[0]);
            else
                errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{p}.letter", Strings.Get("Validation_Letter")));
        }

        var label = ReadMatching(el, "label", p, LabelPattern(), errors) ?? "Dev";

        var caches = new List<string>();
        if (el.TryGetProperty("caches", out var list))
        {
            if (list.ValueKind != JsonValueKind.Array)
            {
                errors.Add(Invalid($"{p}.caches", "array"));
            }
            else
            {
                var index = 0;
                foreach (var item in list.EnumerateArray())
                {
                    var ip = $"{p}.caches[{index++}]";
                    if (item.ValueKind != JsonValueKind.String)
                        errors.Add(Invalid(ip, "string"));
                    else if (!CacheNames.Contains(item.GetString()!, StringComparer.Ordinal))
                        errors.Add(Invalid(ip, string.Join(" | ", CacheNames)));
                    else if (!caches.Contains(item.GetString()!))
                        caches.Add(item.GetString()!);
                }
            }
        }

        return new DevDriveOptions(drivePath ?? "", size, letter, label, caches);
    }

    /// <summary>True for <c>X:\…</c> paths (not UNC) ending in <paramref name="extension"/>, ignoring case.</summary>
    private static bool IsDriveLetterPath(string value, string extension) =>
        value.Length > 3
        && char.IsAsciiLetter(value[0]) && value[1] == ':' && (value[2] is '\\' or '/')
        && value.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

    private static bool IsInside(string candidate, string root)
    {
        if (!System.IO.Path.IsPathFullyQualified(candidate))
            return false;
        try
        {
            var full = System.IO.Path.GetFullPath(candidate);
            var prefix = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(root)) + System.IO.Path.DirectorySeparatorChar;
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>Resolves a relative path against <paramref name="baseDirectory"/>; absolute paths are kept as written.</summary>
    private static string Resolve(string file, string? baseDirectory)
    {
        if (baseDirectory is null || System.IO.Path.IsPathFullyQualified(file))
            return file;
        try
        {
            return System.IO.Path.GetFullPath(file, baseDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return file;
        }
    }

    private static void CheckProperties(JsonElement el, string path, string[] known, List<ProfileError> errors)
    {
        foreach (var prop in el.EnumerateObject())
        {
            if (!known.Contains(prop.Name, StringComparer.Ordinal))
                errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{path}.{prop.Name}",
                    Strings.Format("Validation_UnknownProperty", prop.Name)));
        }
    }

    /// <summary>False when <paramref name="name"/> is absent; reports an error and returns false when it is not an object.</summary>
    private static bool TryObject(
        JsonElement parent, string name, string parentPath, List<ProfileError> errors, out JsonElement element, out string path)
    {
        path = $"{parentPath}.{name}";
        if (!parent.TryGetProperty(name, out element))
            return false;
        if (element.ValueKind == JsonValueKind.Object)
            return true;
        errors.Add(Invalid(path, "object"));
        return false;
    }

    private static string? ReadString(JsonElement el, string name, string p, List<ProfileError> errors, bool required = false)
    {
        if (!el.TryGetProperty(name, out var value))
        {
            if (required)
                errors.Add(Missing($"{p}.{name}", name));
            return null;
        }

        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            errors.Add(Invalid($"{p}.{name}", "string"));
            return null;
        }

        return value.GetString();
    }

    private static string? ReadMatching(JsonElement el, string name, string p, Regex pattern, List<ProfileError> errors)
    {
        var value = ReadString(el, name, p, errors);
        if (value is null || pattern.IsMatch(value))
            return value;
        errors.Add(Pattern($"{p}.{name}", pattern));
        return null;
    }

    private static string? ReadNoControl(JsonElement el, string name, string p, List<ProfileError> errors)
    {
        var value = ReadString(el, name, p, errors);
        if (value is null || !value.Any(char.IsControl))
            return value;
        errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{p}.{name}", Strings.Get("Validation_ControlCharacters")));
        return null;
    }

    private static bool ReadBool(JsonElement el, string name, string p, List<ProfileError> errors)
    {
        if (!el.TryGetProperty(name, out var value))
            return false;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return value.GetBoolean();
        errors.Add(Invalid($"{p}.{name}", "boolean"));
        return false;
    }

    private static ProfileError Missing(string path, string field) =>
        new(ProfileErrorCode.MissingField, path, Strings.Format("Validation_MissingField", field));

    private static ProfileError Invalid(string path, string expected) =>
        new(ProfileErrorCode.InvalidValue, path, Strings.Format("Validation_InvalidValue", expected));

    private static ProfileError Pattern(string path, Regex pattern) =>
        new(ProfileErrorCode.InvalidValue, path,
            Strings.Format("Validation_Pattern", pattern.ToString().Replace(@"\A", "^").Replace(@"\z", "$")));
}
