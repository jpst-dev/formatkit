using System.Text.Json;
using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Cleanup.Localization;
using FormatKit.Cleanup.Steps;
using FormatKit.Core;
using FormatKit.Core.Profiles;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Cleanup;

/// <param name="files">The file system holding the journal; the real one unless a test supplies another.</param>
/// <param name="log">Hears selection notes, such as a power scheme item dropped in favor of a higher-level one.</param>
public sealed class CleanupModule(CleanupCatalog catalog, AppPaths paths, IFileSystem? files = null, Serilog.ILogger? log = null) : IModule
{
    private static readonly string[] KnownProperties = ["enabled", "level", "include", "exclude", "confirmAggressive"];

    private static readonly CleanupCategory[] CategoryOrder = Enum.GetValues<CleanupCategory>();

    public string Name => "cleanup";

    public bool IsEnabled(JsonElement section) => CleanupOptions.From(section).Enabled;

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

        var enabled = false;
        if (!section.TryGetProperty("enabled", out var enabledEl))
            errors.Add(Missing($"{path}.enabled", "enabled"));
        else if (enabledEl.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            errors.Add(Invalid($"{path}.enabled", "boolean"));
        else
            enabled = enabledEl.GetBoolean();

        if (section.TryGetProperty("level", out var level))
        {
            if (level.ValueKind != JsonValueKind.String || !CleanupOptions.LevelNames.ContainsKey(level.GetString()!))
                errors.Add(Invalid($"{path}.level", string.Join(" | ", CleanupOptions.LevelNames.Keys)));
        }
        else if (enabled && !HasInclude(section))
        {
            errors.Add(Missing($"{path}.level", "level"));
        }

        var include = ValidateIds(section, "include", path, errors);
        var exclude = ValidateIds(section, "exclude", path, errors);
        foreach (var (id, index) in exclude)
        {
            if (include.Any(i => i.Id == id))
            {
                errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{path}.exclude[{index}]",
                    Strings.Format("Validation_IncludeExcludeOverlap", id)));
            }
        }

        if (section.TryGetProperty("confirmAggressive", out var confirm) && confirm.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            errors.Add(Invalid($"{path}.confirmAggressive", "boolean"));

        var options = CleanupOptions.From(section);
        if (options.Enabled && !options.ConfirmAggressive)
        {
            var aggressive = ItemSelector.Select(catalog, options).Where(i => i.Level == CleanupLevel.Aggressive).Select(i => i.Id).ToList();
            if (aggressive.Count > 0)
            {
                errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, $"{path}.confirmAggressive",
                    Strings.Format("Validation_ConfirmAggressive", string.Join(", ", aggressive))));
            }
        }

        return errors;
    }

    public IReadOnlyList<IStep> CreateSteps(JsonElement section)
    {
        var options = CleanupOptions.From(section);
        if (!options.Enabled)
            return [];

        var selected = ItemSelector.Select(catalog, options, log);
        var journal = new JournalStore(files ?? new FileSystem(), paths);
        var handlers = new ActionHandlers(() => PowerSchemeHandler.CreatedSchemes(journal.Load().Journal), () => PowerSchemeHandler.FormatKitSchemes(journal.Load().Journal));
        var applier = new ItemApplier(handlers, journal);
        var included = new HashSet<string>(options.Include, StringComparer.Ordinal);

        return CategoryOrder
            .Select(category => selected.Where(i => i.Category == category).ToList())
            .Where(items => items.Count > 0)
            .Select(items => (IStep)new CleanupCategoryStep(items[0].Category, items, included, applier))
            .ToList();
    }

    /// <summary>Developer items are selected by <c>include</c> alone, so a section that includes something needs no level.</summary>
    private static bool HasInclude(JsonElement section) =>
        section.TryGetProperty("include", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0;

    /// <summary>Checks an optional id list; returns its known ids with their indexes.</summary>
    private List<(string Id, int Index)> ValidateIds(JsonElement section, string name, string path, List<ProfileError> errors)
    {
        var ids = new List<(string, int)>();
        if (!section.TryGetProperty(name, out var list))
            return ids;

        if (list.ValueKind != JsonValueKind.Array)
        {
            errors.Add(Invalid($"{path}.{name}", "array"));
            return ids;
        }

        var index = 0;
        foreach (var item in list.EnumerateArray())
        {
            var itemPath = $"{path}.{name}[{index}]";
            if (item.ValueKind != JsonValueKind.String)
                errors.Add(Invalid(itemPath, "string"));
            else if (catalog.Find(item.GetString()!) is null)
                errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, itemPath, Strings.Format("Validation_UnknownItem", item.GetString()!)));
            else
                ids.Add((item.GetString()!, index));
            index++;
        }

        return ids;
    }

    private static ProfileError Missing(string path, string field) =>
        new(ProfileErrorCode.MissingField, path, Strings.Format("Validation_MissingField", field));

    private static ProfileError Invalid(string path, string expected) =>
        new(ProfileErrorCode.InvalidValue, path, Strings.Format("Validation_InvalidValue", expected));
}
