using System.Text.Json;
using FormatKit.Cleanup.Catalog;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Cleanup.Tests;

/// <summary>Builds small catalog items for applier, step and module tests.</summary>
internal static class TestItems
{
    public static CleanupItem Item(
        string id,
        CleanupLevel level = CleanupLevel.Safe,
        ItemScope scope = ItemScope.Machine,
        bool laptopSensitive = false,
        bool requiresReboot = false,
        params CleanupAction[] actions)
    {
        var category = Enum.Parse<CleanupCategory>(id[..id.IndexOf('.')], ignoreCase: true);
        var title = new ItemText($"Title {id}", "W", "B", "G");
        var text = new Dictionary<string, ItemText> { ["en"] = title, ["pt-BR"] = title };
        return new CleanupItem(id, category, level, false, requiresReboot, scope, laptopSensitive, text,
            actions.Length > 0 ? actions : [Dword(@"HKLM\Software\Test", id, 0)]);
    }

    public static RegistryAction Dword(string path, string name, uint value) =>
        new(path, name, RegistryKind.Dword, JsonSerializer.SerializeToElement(value), false);

    public static CleanupCatalog NewCatalog(params CleanupItem[] items) => new(1, items);
}
