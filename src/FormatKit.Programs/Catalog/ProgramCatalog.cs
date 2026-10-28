using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FormatKit.Programs.Catalog;

public enum ProgramCategory { Browsers, Communication, Games, Media, Utilities, Productivity, Development }

public enum InstallContext { Machine, User }

/// <summary>The name and description shown for one catalog program in one language.</summary>
public sealed record ProgramText(string Name, string Description);

/// <summary><paramref name="Scope"/>, when set, is the winget installer scope ("machine" or "user") passed as --scope.</summary>
public sealed record CatalogProgram(
    string Id,
    ProgramCategory Category,
    string WingetId,
    string Source,
    IReadOnlyDictionary<string, ProgramText> Text,
    InstallContext Context = InstallContext.Machine,
    string? Scope = null)
{
    /// <summary>The pt-BR text when the UI culture is Portuguese, else the English text.</summary>
    public ProgramText LocalizedText() =>
        Text[CultureInfo.CurrentUICulture.Name.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? "pt-BR" : "en"];
}

public sealed record ProgramCatalog(int SchemaVersion, IReadOnlyList<CatalogProgram> Programs)
{
    private const string ResourceName = "FormatKit.Programs.Catalog.Data.programs.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static ProgramCatalog LoadEmbedded()
    {
        using var stream = typeof(ProgramCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Parses and validates; throws <see cref="InvalidDataException"/> listing every validator error.</summary>
    public static ProgramCatalog Parse(string json)
    {
        ProgramCatalog catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<ProgramCatalog>(json, Options)
                ?? throw new InvalidDataException("Program catalog JSON is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Program catalog JSON is malformed: " + ex.Message, ex);
        }

        var errors = ProgramCatalogValidator.Validate(catalog);
        if (errors.Count > 0)
            throw new InvalidDataException("Program catalog is invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        return catalog;
    }

    public CatalogProgram? Find(string id) =>
        Programs.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
}
