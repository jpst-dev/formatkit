using System.Globalization;
using System.Windows.Data;
using FormatKit.Cleanup.Catalog;
using FormatKit.Programs.Catalog;

namespace FormatKit.App.Views;

/// <summary>The localized title of a catalog item for the lists.</summary>
public sealed class CatalogTitleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        CleanupItem item => item.LocalizedText().Title,
        CatalogProgram program => program.LocalizedText().Name,
        _ => string.Empty,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
