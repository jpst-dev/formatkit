using System.Windows;

namespace FormatKit.App;

/// <summary>The main window's first size, so it opens whole on a small screen.</summary>
public static class WindowPlacement
{
    /// <summary>The requested size, shrunk to fit <paramref name="workArea"/> but never below <paramref name="minimum"/>.</summary>
    public static Size Fit(Size requested, Rect workArea, Size minimum) => new(
        Math.Max(minimum.Width, Math.Min(requested.Width, workArea.Width)),
        Math.Max(minimum.Height, Math.Min(requested.Height, workArea.Height)));
}
