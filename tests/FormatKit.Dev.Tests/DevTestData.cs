using System.Text.Json;

namespace FormatKit.Dev.Tests;

internal static class DevTestData
{
    public static DevEnvironment Env(int processors = 8) => new(
        @"C:\Users\Joao", @"C:\Users\Joao\Documents", "Joao", "NEW-PC", processors,
        s => s.Replace("%USERPROFILE%", @"C:\Users\Joao", StringComparison.OrdinalIgnoreCase));

    public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>Doubles backslashes so a Windows path can sit inside a JSON string.</summary>
    public static string J(string value) => value.Replace("\\", "\\\\");
}
