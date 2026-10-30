using FormatKit.Prepare;

namespace FormatKit.App.ViewModels;

/// <summary>
/// Icon (Segoe MDL2 Assets glyph) and color (hex, readable on light and dark backgrounds) of each checklist status;
/// the view turns the color into a brush.
/// </summary>
public static class StatusStyle
{
    public static string Glyph(PrepareStatus status) => status switch
    {
        PrepareStatus.Blocker => "",
        PrepareStatus.Attention => "",
        PrepareStatus.NotChecked => "",
        _ => "",
    };

    public static string Color(PrepareStatus status) => status switch
    {
        PrepareStatus.Blocker => "#D13438",
        PrepareStatus.Attention => "#E08A00",
        PrepareStatus.NotChecked => "#5B8DB8",
        _ => "#2E9E5B",
    };
}
