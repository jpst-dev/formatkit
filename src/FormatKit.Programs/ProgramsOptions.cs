using System.Text.Json;
using System.Text.RegularExpressions;
using FormatKit.Core.Profiles;
using FormatKit.Core.SystemAccess;
using FormatKit.Programs.Catalog;
using FormatKit.Programs.Localization;

namespace FormatKit.Programs;

/// <summary><paramref name="Scope"/> comes only from the catalog; profile and import entries leave it null.</summary>
public sealed record WingetEntry(string Id, string Source, string? Version, InstallContext Context, string? Scope = null);

public sealed record DirectEntry(string Name, string? Url, string? Path, string? Sha256, IReadOnlyList<string> Args);

/// <summary><paramref name="Imported"/> holds the entries read from <paramref name="ImportFile"/> during parsing.</summary>
public sealed partial record ProgramsOptions(
    bool AcceptAgreements,
    bool Upgrade,
    IReadOnlyList<string> Catalog,
    IReadOnlyList<WingetEntry> Winget,
    string? ImportFile,
    IReadOnlyList<DirectEntry> Direct,
    IReadOnlyList<WingetEntry>? Imported = null,
    IReadOnlyList<string>? ImportWarnings = null)
{
    /// <summary>Non-null view of <see cref="Imported"/>.</summary>
    public IReadOnlyList<WingetEntry> ImportedPrograms => Imported ?? [];

    /// <summary>Non-null view of <see cref="ImportWarnings"/>: entries skipped while reading the import file.</summary>
    public IReadOnlyList<string> Warnings => ImportWarnings ?? [];

    private static readonly string[] KnownProperties =
        ["enabled", "acceptAgreements", "upgrade", "catalog", "winget", "importFile", "direct"];

    [GeneratedRegex(@"\A[0-9A-Fa-f]{64}\z")]
    private static partial Regex Sha256Pattern();

    /// <summary>
    /// Parses and validates the section; <c>Options</c> is null whenever <c>Errors</c> is not empty.
    /// A relative <c>importFile</c> or direct <c>path</c> resolves against <paramref name="baseDirectory"/> (the profile
    /// file's directory) when it is given, so it still works from a resumed run whose current directory differs.
    /// </summary>
    public static (ProgramsOptions? Options, IReadOnlyList<ProfileError> Errors) Parse(
        JsonElement section, string path, ProgramCatalog catalog, IFileSystem files, string? baseDirectory = null)
    {
        if (section.ValueKind != JsonValueKind.Object)
            return (null, [Invalid(path, "object")]);

        var errors = new List<ProfileError>();

        foreach (var prop in section.EnumerateObject())
        {
            if (!KnownProperties.Contains(prop.Name, StringComparer.Ordinal))
            {
                errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{path}.{prop.Name}",
                    Strings.Format("Validation_UnknownProperty", prop.Name)));
            }
        }

        if (section.TryGetProperty("enabled", out var enabled) && enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            errors.Add(Invalid($"{path}.enabled", "boolean"));

        var accept = ReadBool(section, "acceptAgreements", path, errors);
        var upgrade = ReadBool(section, "upgrade", path, errors);
        var catalogIds = ReadCatalog(section, path, catalog, errors);
        var winget = ReadWinget(section, path, errors);
        var direct = ReadDirect(section, path, errors, baseDirectory);

        string? importFile = null;
        IReadOnlyList<WingetEntry> imported = [];
        var warnings = new List<string>();
        if (section.TryGetProperty("importFile", out var importEl))
        {
            var importPath = $"{path}.importFile";
            if (importEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(importEl.GetString()))
            {
                errors.Add(Invalid(importPath, "string"));
            }
            else
            {
                importFile = Resolve(importEl.GetString()!, baseDirectory);
                if (!files.FileExists(importFile))
                {
                    errors.Add(new ProfileError(ProfileErrorCode.FileNotFound, importPath,
                        Strings.Format("Validation_ImportMissing", importFile)));
                }
                else
                {
                    try
                    {
                        imported = WingetImportFile.Read(files.ReadAllText(importFile), warnings.Add);
                    }
                    catch (InvalidDataException ex)
                    {
                        errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, importPath, ex.Message));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, importPath,
                            Strings.Format("Validation_ImportUnreadable", importFile, ex.Message)));
                    }
                }
            }
        }

        if (!accept && catalogIds.Count + winget.Count + imported.Count + direct.Count > 0)
            errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{path}.acceptAgreements", Strings.Get("Validation_AcceptAgreements")));

        if (errors.Count > 0)
            return (null, errors);

        return (new ProgramsOptions(accept, upgrade, catalogIds, winget, importFile, direct, imported, warnings), errors);
    }

    private static bool ReadBool(JsonElement section, string name, string path, List<ProfileError> errors)
    {
        if (!section.TryGetProperty(name, out var el))
            return false;
        if (el.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return el.GetBoolean();
        errors.Add(Invalid($"{path}.{name}", "boolean"));
        return false;
    }

    private static List<string> ReadCatalog(JsonElement section, string path, ProgramCatalog catalog, List<ProfileError> errors)
    {
        var ids = new List<string>();
        if (!section.TryGetProperty("catalog", out var list))
            return ids;
        if (list.ValueKind != JsonValueKind.Array)
        {
            errors.Add(Invalid($"{path}.catalog", "array"));
            return ids;
        }

        var index = 0;
        foreach (var item in list.EnumerateArray())
        {
            var itemPath = $"{path}.catalog[{index++}]";
            if (item.ValueKind != JsonValueKind.String)
                errors.Add(Invalid(itemPath, "string"));
            else if (catalog.Find(item.GetString()!) is null)
                errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, itemPath, Strings.Format("Validation_UnknownProgram", item.GetString()!)));
            else
                ids.Add(item.GetString()!);
        }

        return ids;
    }

    private static List<WingetEntry> ReadWinget(JsonElement section, string path, List<ProfileError> errors)
    {
        var entries = new List<WingetEntry>();
        if (!section.TryGetProperty("winget", out var list))
            return entries;
        if (list.ValueKind != JsonValueKind.Array)
        {
            errors.Add(Invalid($"{path}.winget", "array"));
            return entries;
        }

        var index = 0;
        foreach (var item in list.EnumerateArray())
        {
            var p = $"{path}.winget[{index++}]";
            if (item.ValueKind != JsonValueKind.Object)
            {
                errors.Add(Invalid(p, "object"));
                continue;
            }

            var before = errors.Count;
            var id = ReadSafe(item, "id", p, required: true, errors);
            var version = ReadSafe(item, "version", p, required: false, errors);

            var source = "winget";
            if (item.TryGetProperty("source", out var s))
            {
                if (s.ValueKind == JsonValueKind.String && s.GetString() is "winget" or "msstore")
                    source = s.GetString()!;
                else
                    errors.Add(Invalid($"{p}.source", "winget | msstore"));
            }

            var context = InstallContext.Machine;
            if (item.TryGetProperty("context", out var c))
            {
                if (c.ValueKind == JsonValueKind.String && c.GetString() == "machine")
                    context = InstallContext.Machine;
                else if (c.ValueKind == JsonValueKind.String && c.GetString() == "user")
                    context = InstallContext.User;
                else
                    errors.Add(Invalid($"{p}.context", "machine | user"));
            }

            if (errors.Count == before)
                entries.Add(new WingetEntry(id!, source, version, context));
        }

        return entries;
    }

    private static string? ReadSafe(JsonElement item, string name, string p, bool required, List<ProfileError> errors)
    {
        if (!item.TryGetProperty(name, out var el))
        {
            if (required)
                errors.Add(Missing($"{p}.{name}", name));
            return null;
        }

        if (el.ValueKind != JsonValueKind.String)
        {
            errors.Add(Invalid($"{p}.{name}", "string"));
            return null;
        }

        try
        {
            return WingetClient.Validate(el.GetString()!, name);
        }
        catch (ArgumentException)
        {
            errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{p}.{name}", Strings.Format("Validation_UnsafeValue", el.GetString()!)));
            return null;
        }
    }

    /// <summary>Resolves a relative path against <paramref name="baseDirectory"/>; absolute paths are kept as written.</summary>
    internal static string Resolve(string file, string? baseDirectory)
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

    private static List<DirectEntry> ReadDirect(JsonElement section, string path, List<ProfileError> errors, string? baseDirectory)
    {
        var entries = new List<DirectEntry>();
        if (!section.TryGetProperty("direct", out var list))
            return entries;
        if (list.ValueKind != JsonValueKind.Array)
        {
            errors.Add(Invalid($"{path}.direct", "array"));
            return entries;
        }

        var index = 0;
        foreach (var item in list.EnumerateArray())
        {
            var p = $"{path}.direct[{index++}]";
            if (item.ValueKind != JsonValueKind.Object)
            {
                errors.Add(Invalid(p, "object"));
                continue;
            }

            var before = errors.Count;
            var name = ReadString(item, "name", p, errors, required: true);
            var url = ReadString(item, "url", p, errors, required: false);
            var filePath = ReadString(item, "path", p, errors, required: false);
            var sha = ReadString(item, "sha256", p, errors, required: false);

            var args = new List<string>();
            if (item.TryGetProperty("args", out var argsEl))
            {
                if (argsEl.ValueKind != JsonValueKind.Array)
                {
                    errors.Add(Invalid($"{p}.args", "array"));
                }
                else
                {
                    var ai = 0;
                    foreach (var a in argsEl.EnumerateArray())
                    {
                        if (a.ValueKind == JsonValueKind.String)
                            args.Add(a.GetString()!);
                        else
                            errors.Add(Invalid($"{p}.args[{ai}]", "string"));
                        ai++;
                    }
                }
            }

            if ((url is null) == (filePath is null))
                errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, p, Strings.Get("Validation_UrlOrPath")));

            string? extension = null;
            if (url is not null)
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                    errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{p}.url", Strings.Get("Validation_HttpsRequired")));
                else
                    extension = System.IO.Path.GetExtension(uri.AbsolutePath);

                if (sha is null)
                    errors.Add(Missing($"{p}.sha256", "sha256"));
            }
            else if (filePath is not null)
            {
                extension = System.IO.Path.GetExtension(filePath);
            }

            if (sha is not null && !Sha256Pattern().IsMatch(sha))
                errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{p}.sha256", Strings.Get("Validation_Sha256")));

            if (extension is not null)
            {
                var isMsi = extension.Equals(".msi", StringComparison.OrdinalIgnoreCase);
                if (!isMsi && !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                    errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, url is not null ? $"{p}.url" : $"{p}.path", Strings.Get("Validation_Extension")));
                else if (isMsi && args.Count > 0)
                    errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{p}.args", Strings.Get("Validation_MsiArgs")));
            }

            if (errors.Count == before)
                entries.Add(new DirectEntry(name!, url, filePath is null ? null : Resolve(filePath, baseDirectory), sha, args));
        }

        return entries;
    }

    private static string? ReadString(JsonElement item, string name, string p, List<ProfileError> errors, bool required)
    {
        if (!item.TryGetProperty(name, out var el))
        {
            if (required)
                errors.Add(Missing($"{p}.{name}", name));
            return null;
        }

        if (el.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(el.GetString()))
        {
            errors.Add(Invalid($"{p}.{name}", "string"));
            return null;
        }

        return el.GetString();
    }

    private static ProfileError Missing(string path, string field) =>
        new(ProfileErrorCode.MissingField, path, Strings.Format("Validation_MissingField", field));

    private static ProfileError Invalid(string path, string expected) =>
        new(ProfileErrorCode.InvalidValue, path, Strings.Format("Validation_InvalidValue", expected));
}
