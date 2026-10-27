using System.Globalization;
using System.Resources;

namespace FormatKit.Cli.Localization;

internal static class Strings
{
    private static readonly ResourceManager Manager =
        new("FormatKit.Cli.Resources.Strings", typeof(Strings).Assembly);

    public static string Get(string name) =>
        Manager.GetString(name, CultureInfo.CurrentUICulture) ?? name;

    public static string Format(string name, params object[] args) =>
        string.Format(CultureInfo.CurrentUICulture, Get(name), args);
}
