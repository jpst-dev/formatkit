using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FormatKit.Cleanup.Diagnosis;

/// <summary>Declaration order is the report order.</summary>
public enum IssueSeverity { Breaks, Security, Contested, Info }

public sealed record IssueText(string Title, string Breaks, string Fix);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DeviceDisabledRule), "deviceDisabled")]
[JsonDerivedType(typeof(ServiceDisabledRule), "serviceDisabled")]
[JsonDerivedType(typeof(RegistryValueRule), "registryValue")]
[JsonDerivedType(typeof(NonBuiltInPowerSchemeRule), "nonBuiltInPowerScheme")]
public abstract record IssueRule;

/// <summary>Matches a disabled device with a hardware id containing <paramref name="HardwareId"/> (case-insensitive).</summary>
public sealed record DeviceDisabledRule(string HardwareId) : IssueRule;

public sealed record ServiceDisabledRule(string Service) : IssueRule;

/// <summary>
/// Exactly one of the two values is set; an absent registry value never matches. <paramref name="Path"/> may contain one
/// <c>*</c> segment, expanded to every subkey.
/// </summary>
public sealed record RegistryValueRule(
    string Path,
    string Name,
    [property: JsonPropertyName("equals")] string? EqualsValue,
    [property: JsonPropertyName("notEquals")] string? NotEqualsValue) : IssueRule;

/// <summary>Matches when the active power scheme is not one of the built-in ones.</summary>
public sealed record NonBuiltInPowerSchemeRule : IssueRule;

public sealed record KnownIssue(
    string Id,
    IssueSeverity Severity,
    IssueRule Rule,
    IReadOnlyDictionary<string, IssueText> Text)
{
    /// <summary>The pt-BR text when the UI culture is Portuguese, else the English text.</summary>
    public IssueText LocalizedText() =>
        Text[CultureInfo.CurrentUICulture.Name.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? "pt-BR" : "en"];
}

public sealed record KnownIssues(int SchemaVersion, IReadOnlyList<KnownIssue> Issues)
{
    private const string ResourceName = "FormatKit.Cleanup.Catalog.Data.known-issues.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static KnownIssues LoadEmbedded()
    {
        using var stream = typeof(KnownIssues).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        using var reader = new StreamReader(stream);
        var issues = Parse(reader.ReadToEnd());
        var errors = KnownIssuesValidator.Validate(issues);
        if (errors.Count > 0)
            throw new InvalidOperationException("Known issues are invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        return issues;
    }

    public static KnownIssues Parse(string json)
        => JsonSerializer.Deserialize<KnownIssues>(json, Options)
           ?? throw new InvalidOperationException("Known issues JSON is empty.");
}
