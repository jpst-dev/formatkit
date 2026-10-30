using System.Globalization;
using System.Windows.Data;
using FormatKit.App.Localization;
using FormatKit.Cleanup.Catalog;

namespace FormatKit.App.Views;

/// <summary>The screen reader name of a catalog list item: its title, plus the opt-in badge for opt-in cleanup items.</summary>
public sealed class CatalogAutomationNameConverter : IValueConverter
{
    private static readonly CatalogTitleConverter Title = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var title = (string)Title.Convert(value, targetType, parameter, culture);
        return value is CleanupItem { IsOptIn: true } ? $"{title}, {Strings.Get("Cat_OptInBadge")}" : title;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
