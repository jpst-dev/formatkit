using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FormatKit.Cleanup.Catalog;

public enum CleanupCategory { Apps, Privacy, Performance }

/// <summary>Declaration order is the level order: each level includes the ones before it.</summary>
public enum CleanupLevel { Safe, Balanced, Aggressive }

public enum ItemScope { User, Machine }

public sealed record CleanupItem(
    string Id,
    CleanupCategory Category,
    CleanupLevel Level,
    bool Contested,
    bool RequiresReboot,
    ItemScope Scope,
    bool LaptopSensitive,
    IReadOnlyDictionary<string, ItemText> Text,
    IReadOnlyList<CleanupAction> Actions)
{
    /// <summary>The pt-BR text when the UI culture is Portuguese, else the English text.</summary>
    public ItemText LocalizedText() =>
        Text[CultureInfo.CurrentUICulture.Name.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? "pt-BR" : "en"];
}

public sealed record CleanupCatalog(int SchemaVersion, IReadOnlyList<CleanupItem> Items)
{
    private const string ResourceName = "FormatKit.Cleanup.Catalog.Data.cleanup.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static CleanupCatalog LoadEmbedded()
    {
        using var stream = typeof(CleanupCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        using var reader = new StreamReader(stream);
        var catalog = Parse(reader.ReadToEnd());
        var errors = CatalogValidator.Validate(catalog);
        if (errors.Count > 0)
            throw new InvalidOperationException("Cleanup catalog is invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        return catalog;
    }

    public static CleanupCatalog Parse(string json)
        => JsonSerializer.Deserialize<CleanupCatalog>(json, Options)
           ?? throw new InvalidOperationException("Cleanup catalog JSON is empty.");

    public CleanupItem? Find(string id) =>
        Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));
}
