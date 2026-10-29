using System.Globalization;

namespace FormatKit.Prepare;

internal static class Sizes
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>Binary units with one decimal (<c>1.2 GB</c>), in the current UI culture.</summary>
    public static string Format(long bytes)
    {
        if (bytes < 1024)
            return string.Create(CultureInfo.CurrentUICulture, $"{bytes} B");
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return string.Create(CultureInfo.CurrentUICulture, $"{value:F1} {Units[unit]}");
    }
}
