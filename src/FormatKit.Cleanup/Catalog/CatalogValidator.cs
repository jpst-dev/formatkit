using System.Text.Json;
using System.Text.RegularExpressions;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Cleanup.Catalog;

/// <summary>Enforces the catalog rules of spec 5.2. Returns one message per violation; empty means valid.</summary>
public static partial class CatalogValidator
{
    private static readonly string[] Languages = ["en", "pt-BR"];

    [GeneratedRegex("^(apps|startup|privacy|ai|interface|appearance|input|sound|performance|power|developer)\\.[a-z0-9-]+$")]
    private static partial Regex IdPattern();

    public static IReadOnlyList<string> Validate(CleanupCatalog catalog)
    {
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in catalog.Items)
        {
            if (!seen.Add(item.Id))
                errors.Add($"{item.Id}: duplicate id");
            ValidateItem(item, errors);
        }
        return errors;
    }

    private static void ValidateItem(CleanupItem item, List<string> errors)
    {
        void Error(string message) => errors.Add($"{item.Id}: {message}");

        var prefix = item.Category.ToString().ToLowerInvariant();
        if (!IdPattern().IsMatch(item.Id))
            Error("id must match <category>.<kebab-name>");
        else if (!item.Id.StartsWith(prefix + ".", StringComparison.Ordinal))
            Error($"id prefix must equal category '{prefix}'");

        foreach (var language in Languages)
        {
            if (item.Text is null || !item.Text.TryGetValue(language, out var text) || text is null)
            {
                Error($"missing text for language '{language}'");
                continue;
            }
            CheckField(language, "title", text.Title);
            CheckField(language, "what", text.What);
            CheckField(language, "breaks", text.Breaks);
            CheckField(language, "gain", text.Gain);
        }

        void CheckField(string language, string field, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                Error($"text '{language}' field '{field}' is blank");
        }

        if (item.Contested && item.Level != CleanupLevel.Aggressive)
            Error("contested items must be at level aggressive");

        var actions = item.Actions ?? [];
        if (actions.Count == 0)
            Error("item has no actions");

        if (item.LaptopSensitive && !actions.Any(a => a is PowerSchemeAction or PowerSettingAction))
            Error("laptopSensitive requires a powerScheme or powerSetting action");

        foreach (var action in actions)
        {
            switch (action)
            {
                case RegistryAction registry:
                    ValidateRegistry(registry, Error);
                    break;
                case ServiceAction service:
                    if (ProtectedLists.Services.Contains(service.Name))
                        Error($"service '{service.Name}' is protected");
                    break;
                case AppxAction appx:
                    if (!ProtectedLists.RemovableApps.Contains(appx.Package) || ProtectedLists.IsNeverRemove(appx.Package))
                        Error($"appx package '{appx.Package}' is not in the removable-app allowlist");
                    if (string.IsNullOrWhiteSpace(appx.StoreId))
                        Error($"appx package '{appx.Package}' has no storeId");
                    break;
                case PowerSchemeAction power:
                    if (power.Scheme is not ("balanced" or "highPerformance" or "ultimate" or "formatkit"))
                        Error($"unknown power scheme '{power.Scheme}'");
                    break;
                case OptionalFeatureAction feature:
                    if (feature.State == OptionalFeatureState.Absent)
                        Error($"optionalFeature '{feature.Name}' state must be enabled or disabled");
                    break;
                case ScheduledTaskAction task:
                    var taskPath = task.Path ?? "";
                    var taskName = task.Name ?? "";
                    if (taskPath.IndexOfAny(['*', '?', '[']) >= 0 || taskName.IndexOfAny(['*', '?', '[']) >= 0)
                        Error($"scheduledTask '{taskPath}{taskName}' must not contain wildcards (*, ? or [)");
                    else if (!taskPath.StartsWith('\\') || !taskPath.EndsWith('\\'))
                        Error($"scheduledTask path '{taskPath}' must start and end with a backslash");
                    else if (string.IsNullOrWhiteSpace(taskName) || taskName.Contains('\\'))
                        Error($"scheduledTask name '{taskName}' must be a single task name");
                    else if (ProtectedLists.IsProtectedTask(taskPath, taskName))
                        Error($"scheduledTask '{taskPath}{taskName}' is protected");
                    break;
                case StartupItemAction startup:
                    if (string.IsNullOrWhiteSpace(startup.Name) || startup.Name.Contains('\\'))
                        Error($"startupItem name '{startup.Name}' must be a single Run value name");
                    break;
                case PowerSettingAction setting:
                    if (!Guid.TryParse(setting.Subgroup, out _))
                        Error($"powerSetting subgroup '{setting.Subgroup}' must be a GUID");
                    if (!Guid.TryParse(setting.Setting, out _))
                        Error($"powerSetting setting '{setting.Setting}' must be a GUID");
                    break;
                case NamedAction named:
                    if (!ProtectedLists.NamedHandlers.Contains(named.Handler))
                        Error($"unknown named handler '{named.Handler}'");
                    break;
            }
        }

        if (item.Scope == ItemScope.User
            && !actions.All(a => a is RegistryAction r && r.Path.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase)
                || a is StartupItemAction { Scope: ItemScope.User }))
            Error("scope user requires only HKCU registry or user-scope startupItem actions");
    }

    private static void ValidateRegistry(RegistryAction registry, Action<string> error)
    {
        var path = registry.Path ?? "";
        if (!path.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
            error($"registry path '{path}' must start with HKLM\\ or HKCU\\");

        if (registry.IfFileExists is not null && string.IsNullOrWhiteSpace(registry.IfFileExists))
            error($"registry value '{registry.Name}' has a blank ifFileExists");

        if (registry.Delete)
            return;

        if (registry.Kind is null || registry.Value is null)
        {
            error($"registry value '{registry.Name}' needs kind and value (or delete)");
            return;
        }

        var value = registry.Value.Value;
        var valid = registry.Kind switch
        {
            RegistryKind.Dword => value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out _),
            RegistryKind.Qword => value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out _),
            RegistryKind.String => value.ValueKind == JsonValueKind.String,
            RegistryKind.Binary => value.ValueKind == JsonValueKind.String && IsHex(value.GetString()!),
            _ => false,
        };
        if (!valid)
            error($"registry value '{registry.Name}' does not match kind {registry.Kind}");
    }

    private static bool IsHex(string text) =>
        text.Length % 2 == 0 && text.All(Uri.IsHexDigit);
}
