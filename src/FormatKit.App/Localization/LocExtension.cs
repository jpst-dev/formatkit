using System.Windows.Markup;

namespace FormatKit.App.Localization;

/// <summary>XAML access to <see cref="Strings"/>: <c>{loc:Loc Home_Intro}</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(key);
}
