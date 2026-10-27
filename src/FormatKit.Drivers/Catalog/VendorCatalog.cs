using System.Text.Json;
using System.Text.Json.Serialization;
using FormatKit.Core.Hardware;

namespace FormatKit.Drivers.Catalog;

/// <summary>OEM driver tool. <see cref="Executable"/> may contain environment variables; expand with Environment.ExpandEnvironmentVariables at use.</summary>
public sealed record OemTool(
    string Id,
    IReadOnlyList<string> ManufacturerMatches,
    string WingetId,
    string Executable,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<int> SuccessExitCodes,
    IReadOnlyList<int> RebootExitCodes);

public sealed record GpuPackage(GpuVendor Vendor, string WingetId);

public sealed record BoardSupport(IReadOnlyList<string> ManufacturerMatches, string SupportUrl);

public sealed record VendorCatalog(
    IReadOnlyList<OemTool> Oem,
    IReadOnlyList<GpuPackage> Gpu,
    IReadOnlyList<BoardSupport> Boards)
{
    private const string ResourceName = "FormatKit.Drivers.Catalog.Data.vendors.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static VendorCatalog LoadEmbedded()
    {
        using var stream = typeof(VendorCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static VendorCatalog Parse(string json)
        => JsonSerializer.Deserialize<VendorCatalog>(json, Options)
           ?? throw new InvalidOperationException("Vendor catalog JSON is empty.");
}
