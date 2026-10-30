using FormatKit.App.Localization;

namespace FormatKit.App.ViewModels;

/// <summary>Stands in for a page that a later task fills.</summary>
public sealed class PlaceholderViewModel(Page page)
{
    public Page Page { get; } = page;

    public string Title { get; } = Strings.Get($"Nav_{page}");
}
