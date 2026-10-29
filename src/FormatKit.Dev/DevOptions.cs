using System.Text.Json;
using System.Text.RegularExpressions;
using FormatKit.Core.Profiles;
using FormatKit.Dev.Localization;

namespace FormatKit.Dev;

public sealed record WslConfig(string? Memory, int? Processors, string? Swap);

public sealed record WslOptions(string Distro, string User, WslConfig? Config);

public sealed record GitOptions(string? Name, string? Email, string? DefaultBranch, string? Editor, string? AutoCrlf);

public sealed record SshOptions(string KeyType, string Comment);

/// <summary><paramref name="To"/> is expanded and normalized (<c>GetFullPath</c>), inside the user profile.</summary>
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
    /// <summary>The only plugins cloned from GitHub; every other plugin name is one Oh My Zsh ships with.</summary>
    internal static readonly string[] ExternalPlugins = ["zsh-autosuggestions", "zsh-syntax-highlighting"];
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

    /// <summary>An https address with anything but a host before the first '/', '?' or '#' carries credentials.</summary>
    [GeneratedRegex(@"\Ahttps://[^/?#]*@")]
    private static partial Regex RepoCredentialsPattern();

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
        var shell = ReadShell(section, path, section.TryGetProperty("wsl", out _), errors);
        var dotfiles = ReadDotfiles(section, path, baseDirectory, env, errors, OwnedFiles(env, wsl, git, ssh, shell));
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

        var name = RejectLeadingDash(ReadNoControl(el, "name", p, errors), $"{p}.name", errors);
        var email = ReadNoControl(el, "email", p, errors);
        if (email is not null && !email.Contains('@'))
            errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{p}.email", Strings.Get("Validation_EmailAt")));
        var branch = RejectLeadingDash(ReadMatching(el, "defaultBranch", p, BranchPattern(), errors), $"{p}.defaultBranch", errors);
        var editor = RejectLeadingDash(ReadNoControl(el, "editor", p, errors), $"{p}.editor", errors);

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

        var comment = ReadMatching(el, "comment", p, CommentPattern(), errors) ?? DefaultComment(env);

        return new SshOptions(keyType, comment);
    }

    /// <summary><c>user@computer</c> reduced to the comment pattern: other characters become '-', lowercase, at most 100.</summary>
    private static string DefaultComment(DevEnvironment env)
    {
        var raw = $"{env.UserName}@{env.ComputerName}".ToLowerInvariant();
        var safe = string.Concat(raw.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '@' or '+' or '-' ? c : '-'));
        return safe.Length > 100 ? safe[..100] : safe;
    }

    /// <summary>The files other present subsections write, by full path (ignoring case), with the subsection that owns each.</summary>
    private static Dictionary<string, string> OwnedFiles(
        DevEnvironment env, WslOptions? wsl, GitOptions? git, SshOptions? ssh, ShellOptions? shell)
    {
        var owned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (git is not null)
            owned[System.IO.Path.Combine(env.UserProfile, ".gitconfig")] = "modules.dev.git";
        if (ssh is not null)
            owned.TryAdd(System.IO.Path.Combine(env.UserProfile, ".gitconfig"), "modules.dev.ssh");
        if (wsl?.Config is not null)
            owned[System.IO.Path.Combine(env.UserProfile, ".wslconfig")] = "modules.dev.wsl.wslconfig";
        if (shell?.Windows is { OhMyPosh: true })
        {
            foreach (var documents in new[] { env.Documents, System.IO.Path.Combine(env.UserProfile, "Documents") })
            foreach (var folder in new[] { "PowerShell", "WindowsPowerShell" })
                owned[System.IO.Path.Combine(documents, folder, "Microsoft.PowerShell_profile.ps1")] = "modules.dev.shell.windows.ohMyPosh";
        }

        return owned;
    }

    private static DotfilesOptions? ReadDotfiles(
        JsonElement section, string parent, string? baseDirectory, DevEnvironment env, List<ProfileError> errors,
        IReadOnlyDictionary<string, string> ownedFiles)
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
        else if (repo is not null && RepoCredentialsPattern().IsMatch(repo))
            errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{p}.repo", Strings.Get("Validation_RepoCredentials")));
        if (localPath is not null)
            localPath = Resolve(env.Expand(localPath), baseDirectory);

        var cloneTo = env.Expand(ReadString(el, "cloneTo", p, errors) ?? @"%USERPROFILE%\dotfiles");
        if (TryNormalize(cloneTo) is { } fullCloneTo && !fullCloneTo.StartsWith(@"\\", StringComparison.Ordinal)
            && System.IO.Path.IsPathFullyQualified(cloneTo))
            cloneTo = fullCloneTo;
        else
            errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{p}.cloneTo", Strings.Get("Validation_CloneToAbsolute")));

        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                    else
                    {
                        to = System.IO.Path.GetFullPath(to);
                        if (!seenTargets.Add(to))
                        {
                            errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{fp}.to", Strings.Get("Validation_DuplicateTo")));
                            ok = false;
                        }
                        else if (ownedFiles.TryGetValue(to, out var owner))
                        {
                            errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{fp}.to",
                                Strings.Format("Validation_DotfileOwned", owner)));
                            ok = false;
                        }
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

            var theme = RejectReservedName(ReadMatching(w, "theme", wp, ThemePattern(), errors), $"{wp}.theme", errors) ?? "robbyrussell";

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
                    {
                        var plugin = RejectReservedName(item.GetString()!, ip, errors);
                        if (plugin is not null && !plugins.Contains(plugin, StringComparer.Ordinal))
                            plugins.Add(plugin);
                    }
                }
            }

            wsl = new WslShellOptions(zsh, ohMyZsh, theme, plugins);
        }

        WindowsShellOptions? windows = null;
        if (TryObject(el, "windows", p, errors, out var win, out var winp))
        {
            CheckProperties(win, winp, ["ohMyPosh", "theme", "font"], errors);
            var ohMyPosh = ReadBool(win, "ohMyPosh", winp, errors);
            var theme = RejectReservedName(ReadMatching(win, "theme", winp, ThemePattern(), errors), $"{winp}.theme", errors) ?? "paradox";

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
        else if (drivePath is not null && !IsPlainFilePath(drivePath))
            errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{p}.path", Strings.Get("Validation_VhdxPathChars")));

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

    private static readonly char[] ForbiddenPathChars = [.. System.IO.Path.GetInvalidPathChars(), '"', '*', '?', '<', '>', '|'];

    /// <summary>
    /// False for a path with characters Windows rejects (and PowerShell quoting should never have to cope with),
    /// or one naming a file straight in a drive root with an empty name before the extension, such as <c>C:\.vhdx</c>.
    /// </summary>
    private static bool IsPlainFilePath(string value)
    {
        if (value.IndexOfAny(ForbiddenPathChars) >= 0)
            return false;
        var lastSeparator = value.LastIndexOfAny(['\\', '/']);
        var inRoot = lastSeparator == 2; // "X:\" is the only thing before the file name
        return !(inRoot && string.IsNullOrWhiteSpace(System.IO.Path.GetFileNameWithoutExtension(value[(lastSeparator + 1)..])));
    }

    private static string? TryNormalize(string path)
    {
        try
        {
            return System.IO.Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>True when <paramref name="candidate"/> (fully qualified) normalizes to a path below <paramref name="root"/>.</summary>
    internal static bool IsInside(string candidate, string root)
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

    /// <summary>A theme or plugin name that starts with '-' or is '.' or '..' would be taken as an option or a path by the commands it reaches.</summary>
    private static string? RejectReservedName(string? value, string path, List<ProfileError> errors)
    {
        if (value is null)
            return null;
        if (value.StartsWith('-'))
            errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, path, Strings.Get("Validation_LeadingDash")));
        else if (value is "." or "..")
            errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, path, Strings.Get("Validation_ReservedName")));
        else
            return value;
        return null;
    }

    /// <summary>A value starting with '-' would be taken by git as an option.</summary>
    private static string? RejectLeadingDash(string? value, string path, List<ProfileError> errors)
    {
        if (value is null || !value.StartsWith('-'))
            return value;
        errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, path, Strings.Get("Validation_LeadingDash")));
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
