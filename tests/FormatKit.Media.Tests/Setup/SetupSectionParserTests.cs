using FormatKit.Core.Profiles;
using FormatKit.Media.Setup;

namespace FormatKit.Media.Tests.Setup;

public class SetupSectionParserTests
{
    private static (SetupOptions? Options, IReadOnlyList<ProfileError> Errors) P(string setup) =>
        SetupSectionParser.Parse("{ \"setup\": " + setup + " }");

    private static void AssertError(string setup, string path)
    {
        var (_, errors) = P(setup);
        Assert.Contains(errors, e => e.Path == path);
    }

    [Fact]
    public void AbsentSetupGivesDefaults()
    {
        var (o, errors) = SetupSectionParser.Parse("{ \"schemaVersion\": 1 }");
        Assert.Empty(errors);
        Assert.Equal(new SetupOptions(), o);
    }

    [Fact]
    public void FullExample()
    {
        var (o, errors) = SetupSectionParser.Parse("""
            // comment
            { "setup": { "account": "microsoft", "edition": "Windows 11 Pro", "computerName": "PC-JOAO",
              "language": "pt-BR", "keyboard": "0416:00010416", "timeZone": "UTC", }, }
            """);
        Assert.Empty(errors);
        Assert.Equal(new SetupOptions(SetupAccount.Microsoft, "Windows 11 Pro", "PC-JOAO", "pt-BR", "0416:00010416", "UTC"), o);
    }

    [Fact]
    public void LocalAccountParses() => Assert.Equal(SetupAccount.Local, P("{ \"account\": \"local\" }").Options!.Account);

    [Fact]
    public void NotAnObject() => AssertError("5", "setup");

    [Fact]
    public void BadAccount() => AssertError("{ \"account\": \"xyz\" }", "setup.account");

    [Theory]
    [InlineData("")]
    [InlineData("ABCDEFGHIJKLMNOP")]
    [InlineData("PC_1")]
    [InlineData("12345")]
    public void BadComputerName(string name) => AssertError($"{{ \"computerName\": \"{name}\" }}", "setup.computerName");

    [Fact]
    public void BadTimeZone() => AssertError("{ \"timeZone\": \"Mars\" }", "setup.timeZone");

    [Fact]
    public void BadLanguage() => AssertError("{ \"language\": \"xx-INVALID\" }", "setup.language");

    [Theory]
    [InlineData("0416:00010416")]
    [InlineData("pt-BR")]
    public void GoodKeyboard(string k) => Assert.Empty(P($"{{ \"keyboard\": \"{k}\" }}").Errors);

    [Fact]
    public void BadKeyboard() => AssertError("{ \"keyboard\": \"zz\" }", "setup.keyboard");

    [Fact]
    public void NonStringFieldIsInvalid() => AssertError("{ \"edition\": 3 }", "setup.edition");

    [Theory]
    [InlineData("foo")]
    [InlineData("password")]
    public void UnknownField(string name)
    {
        var (_, errors) = P($"{{ \"{name}\": \"x\" }}");
        Assert.Contains(errors, e => e.Path == "setup." + name && e.Code == ProfileErrorCode.InvalidValue);
    }
}
