using System.Text.Json;
using FormatKit.Core.Profiles;
using FormatKit.Core.Steps;
using FormatKit.Drivers.Catalog;
using FormatKit.Drivers.Localization;
using FormatKit.Drivers.Steps;

namespace FormatKit.Drivers;

public sealed class DriversModule(VendorCatalog catalog, TimeSpan networkRetryDelay) : IModule
{
    private static readonly string[] KnownProperties = ["enabled", "sources", "networkDriverBackup"];

    public string Name => "drivers";

    public bool IsEnabled(JsonElement section) => DriversOptions.From(section, null).Enabled;

    public IReadOnlyList<ProfileError> Validate(JsonElement section, string path)
    {
        if (section.ValueKind != JsonValueKind.Object)
            return [Invalid(path, "object")];

        var errors = new List<ProfileError>();

        foreach (var prop in section.EnumerateObject())
        {
            if (!KnownProperties.Contains(prop.Name, StringComparer.Ordinal))
            {
                errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{path}.{prop.Name}",
                    Strings.Format("Validation_UnknownProperty", prop.Name)));
            }
        }

        if (!section.TryGetProperty("enabled", out var enabled))
            errors.Add(Missing($"{path}.enabled", "enabled"));
        else if (enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            errors.Add(Invalid($"{path}.enabled", "boolean"));

        if (!section.TryGetProperty("sources", out var sources))
            errors.Add(Missing($"{path}.sources", "sources"));
        else
            ValidateSources(sources, $"{path}.sources", errors);

        if (section.TryGetProperty("networkDriverBackup", out var backup) && backup.ValueKind != JsonValueKind.String)
            errors.Add(Invalid($"{path}.networkDriverBackup", "string"));

        return errors;
    }

    /// <remarks>A relative <c>networkDriverBackup</c> resolves against the profile folder (<paramref name="baseDirectory"/>),
    /// like Programs and Dev; without one it resolves against the current directory.</remarks>
    public IReadOnlyList<ExternalInput> ExternalInputs(JsonElement section, string? baseDirectory) =>
        DriversOptions.From(section, baseDirectory).NetworkDriverBackup is { Length: > 0 } backup
            ? [new ExternalInput("modules.drivers.networkDriverBackup", Path.GetFullPath(backup), ExternalInputKind.Folder)]
            : [];

    public IReadOnlyList<IStep> CreateSteps(JsonElement section) => CreateSteps(section, null);

    /// <param name="baseDirectory">The profile file's directory; a relative networkDriverBackup resolves against it.</param>
    public IReadOnlyList<IStep> CreateSteps(JsonElement section, string? baseDirectory)
    {
        var options = DriversOptions.From(section, baseDirectory);
        var steps = new List<IStep> { new NetworkCheckStep(options.NetworkDriverBackup, networkRetryDelay) };

        if (options.Sources.Contains(DriverSource.WindowsUpdate))
            steps.Add(new WindowsUpdateDriversStep());
        if (options.Sources.Contains(DriverSource.Oem))
            steps.Add(new OemToolStep(catalog));
        if (options.Sources.Contains(DriverSource.Gpu))
            steps.Add(new GpuStep(catalog));

        steps.Add(new LeftoverReportStep(catalog));
        return steps;
    }

    private static void ValidateSources(JsonElement sources, string path, List<ProfileError> errors)
    {
        if (sources.ValueKind != JsonValueKind.Array)
        {
            errors.Add(Invalid(path, "array"));
            return;
        }

        if (sources.GetArrayLength() == 0)
        {
            errors.Add(Invalid(path, "non-empty array"));
            return;
        }

        var seen = new HashSet<DriverSource>();
        var index = 0;
        foreach (var item in sources.EnumerateArray())
        {
            var itemPath = $"{path}[{index++}]";
            if (item.ValueKind != JsonValueKind.String)
            {
                errors.Add(Invalid(itemPath, "string"));
            }
            else if (!DriversOptions.SourceNames.TryGetValue(item.GetString()!, out var source))
            {
                errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, itemPath,
                    Strings.Format("Validation_UnknownSource", item.GetString()!, string.Join(", ", DriversOptions.SourceNames.Keys))));
            }
            else if (!seen.Add(source))
            {
                errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, itemPath,
                    Strings.Format("Validation_DuplicateSource", item.GetString()!)));
            }
        }
    }

    private static ProfileError Missing(string path, string field) =>
        new(ProfileErrorCode.MissingField, path, Strings.Format("Validation_MissingField", field));

    private static ProfileError Invalid(string path, string expected) =>
        new(ProfileErrorCode.InvalidValue, path, Strings.Format("Validation_InvalidValue", expected));
}
