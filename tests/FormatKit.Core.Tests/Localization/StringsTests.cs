using System.Globalization;
using FormatKit.Core.Localization;

namespace FormatKit.Core.Tests.Localization;

[Collection("Culture")]
public class StringsTests : IDisposable
{
    private readonly CultureInfo _original = CultureInfo.CurrentUICulture;

    public void Dispose() => CultureInfo.CurrentUICulture = _original;

    [Fact]
    public void Portuguese_culture_returns_Portuguese_text()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("pt-BR");

        Assert.Contains("encontrado", Strings.Get("ProfileError_FileNotFound"));
    }

    [Fact]
    public void English_culture_returns_English_text()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");

        Assert.Contains("not found", Strings.Get("ProfileError_FileNotFound"));
    }

    [Fact]
    public void Missing_key_returns_the_key()
    {
        Assert.Equal("NoSuchKey", Strings.Get("NoSuchKey"));
    }

    [Fact]
    public void Format_substitutes_arguments()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");

        Assert.Contains("2", Strings.Format("ProfileError_UnsupportedSchemaVersion", 2, 1));
    }
}
