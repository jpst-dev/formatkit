using System.Security.Cryptography;
using System.Text.Json;
using FormatKit.Core.Localization;

namespace FormatKit.Core.Profiles;

public sealed class ProfileLoader(IReadOnlyList<IModule> modules)
{
    public const int CurrentSchemaVersion = 1;

    public ProfileLoadResult Load(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Fail(Error(ProfileErrorCode.FileNotFound, "",Strings.Format("ProfileError_FileNotFound", path)));
        }

        return ParseBytes(bytes, path);
    }

    public ProfileLoadResult Parse(string json, string path) =>
        ParseBytes(System.Text.Encoding.UTF8.GetBytes(json), path);

    private ProfileLoadResult ParseBytes(byte[] bytes, string path)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(StripUtf8Bom(bytes), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException ex)
        {
            return Fail(Error(ProfileErrorCode.InvalidJson, "", Strings.Format("ProfileError_InvalidJson", ex.Message)));
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Fail(Error(ProfileErrorCode.InvalidValue, "", Strings.Format("ProfileError_InvalidValue", "object")));

            var errors = new List<ProfileError>();

            var schemaVersion = 0;
            if (!root.TryGetProperty("schemaVersion", out var sv))
                errors.Add(Missing("schemaVersion"));
            else if (sv.ValueKind != JsonValueKind.Number || !sv.TryGetInt32(out schemaVersion))
                errors.Add(Invalid("schemaVersion", "integer"));
            else if (schemaVersion != CurrentSchemaVersion)
                errors.Add(Error(ProfileErrorCode.UnsupportedSchemaVersion, "schemaVersion",
                    Strings.Format("ProfileError_UnsupportedSchemaVersion", schemaVersion, CurrentSchemaVersion)));

            string? name = null;
            if (!root.TryGetProperty("name", out var nameEl))
                errors.Add(Missing("name"));
            else if (nameEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name = nameEl.GetString()))
                errors.Add(Invalid("name", "non-empty string"));

            var sections = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (!root.TryGetProperty("modules", out var modulesEl))
                errors.Add(Missing("modules"));
            else if (modulesEl.ValueKind != JsonValueKind.Object)
                errors.Add(Invalid("modules", "object"));
            else
            {
                foreach (var prop in modulesEl.EnumerateObject())
                {
                    var modulePath = "modules." + prop.Name;
                    var module = modules.FirstOrDefault(m => m.Name == prop.Name);
                    if (module is null)
                    {
                        errors.Add(Error(ProfileErrorCode.UnknownModule, modulePath,
                            Strings.Format("ProfileError_UnknownModule", prop.Name)));
                        continue;
                    }

                    var section = prop.Value.Clone();
                    sections[prop.Name] = section;
                    errors.AddRange(module.Validate(section, modulePath));
                }
            }

            if (errors.Count > 0)
                return new ProfileLoadResult(null, errors);

            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var profile = new Profile(schemaVersion, name!, sections);
            return new ProfileLoadResult(new LoadedProfile(profile, path, hash), []);
        }
    }

    private static ReadOnlyMemory<byte> StripUtf8Bom(byte[] bytes) =>
        bytes.AsSpan().StartsWith(System.Text.Encoding.UTF8.Preamble) ? bytes.AsMemory(System.Text.Encoding.UTF8.Preamble.Length) : bytes;

    private static ProfileError Missing(string field) =>
        Error(ProfileErrorCode.MissingField, field, Strings.Format("ProfileError_MissingField", field));

    private static ProfileError Invalid(string path, string expected) =>
        Error(ProfileErrorCode.InvalidValue, path, Strings.Format("ProfileError_InvalidValue", expected));

    private static ProfileError Error(ProfileErrorCode code, string path, string message) => new(code, path, message);

    private static ProfileLoadResult Fail(ProfileError error) => new(null, [error]);
}
