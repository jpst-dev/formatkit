using System.Xml.Linq;
using FormatKit.Media.Setup;
using FormatKit.Media.Unattend;

namespace FormatKit.Media.Tests.Unattend;

public class UnattendBuilderTests
{
    private static readonly XNamespace Ns = "urn:schemas-microsoft-com:unattend";

    private static ResolvedSetup Setup(
        SetupAccount account = SetupAccount.Local, string? edition = null, string? computerName = null) =>
        new(account, edition, computerName, "pt-BR", "0416:00010416", "E. South America Standard Time");

    private static XElement Parse(ResolvedSetup setup, bool withProfile = false) =>
        XDocument.Parse(UnattendBuilder.Build(setup, withProfile)).Root!;

    private static XElement Component(XElement root, string pass, string name) =>
        root.Elements(Ns + "settings").Single(s => (string?)s.Attribute("pass") == pass)
            .Elements(Ns + "component").Single(c => (string?)c.Attribute("name") == name);

    private static string? Value(XElement parent, params string[] path)
    {
        XElement? current = parent;
        foreach (var name in path)
        {
            current = current?.Element(Ns + name);
        }
        return current?.Value;
    }

    [Fact]
    public void Has_three_passes_and_amd64_components()
    {
        var root = Parse(Setup());
        Assert.Equal(["windowsPE", "specialize", "oobeSystem"],
            root.Elements(Ns + "settings").Select(s => (string?)s.Attribute("pass")).ToArray());
        var components = root.Descendants(Ns + "component").ToList();
        Assert.NotEmpty(components);
        Assert.All(components, c => Assert.Equal("amd64", (string?)c.Attribute("processorArchitecture")));
    }

    [Fact]
    public void WindowsPE_sets_language_and_keyboard()
    {
        var pe = Component(Parse(Setup()), "windowsPE", "Microsoft-Windows-International-Core-WinPE");
        Assert.Equal("pt-BR", Value(pe, "SetupUILanguage", "UILanguage"));
        Assert.Equal("pt-BR", Value(pe, "UILanguage"));
        Assert.Equal("pt-BR", Value(pe, "SystemLocale"));
        Assert.Equal("pt-BR", Value(pe, "UserLocale"));
        Assert.Equal("0416:00010416", Value(pe, "InputLocale"));
    }

    [Fact]
    public void Setup_accepts_eula()
    {
        var setup = Component(Parse(Setup()), "windowsPE", "Microsoft-Windows-Setup");
        Assert.Equal("true", Value(setup, "UserData", "AcceptEula"));
    }

    [Fact]
    public void Edition_selects_image_and_generic_key()
    {
        var setup = Component(Parse(Setup(edition: "Windows 11 Pro")), "windowsPE", "Microsoft-Windows-Setup");
        var meta = setup.Element(Ns + "ImageInstall")!.Element(Ns + "OSImage")!
            .Element(Ns + "InstallFrom")!.Element(Ns + "MetaData")!;
        Assert.Equal("/IMAGE/NAME", Value(meta, "Key"));
        Assert.Equal("Windows 11 Pro", Value(meta, "Value"));
        var key = setup.Element(Ns + "UserData")!.Element(Ns + "ProductKey")!;
        Assert.Equal("VK7JG-NPHTM-C97JM-9MPGT-3V66T", Value(key, "Key"));
        Assert.Equal("OnError", Value(key, "WillShowUI"));
    }

    [Fact]
    public void No_edition_means_no_image_install_or_key()
    {
        var setup = Component(Parse(Setup()), "windowsPE", "Microsoft-Windows-Setup");
        Assert.Null(setup.Element(Ns + "ImageInstall"));
        Assert.Null(setup.Element(Ns + "UserData")!.Element(Ns + "ProductKey"));
    }

    [Fact]
    public void Specialize_has_time_zone_and_optional_computer_name()
    {
        var without = Component(Parse(Setup()), "specialize", "Microsoft-Windows-Shell-Setup");
        Assert.Equal("E. South America Standard Time", Value(without, "TimeZone"));
        Assert.Null(without.Element(Ns + "ComputerName"));

        var with = Component(Parse(Setup(computerName: "FK-PC")), "specialize", "Microsoft-Windows-Shell-Setup");
        Assert.Equal("FK-PC", Value(with, "ComputerName"));
        Assert.Equal("E. South America Standard Time", Value(with, "TimeZone"));
    }

    [Fact]
    public void Oobe_local_account_hides_online_account_and_wireless_pages()
    {
        var oobe = Component(Parse(Setup(SetupAccount.Local)), "oobeSystem", "Microsoft-Windows-Shell-Setup")
            .Element(Ns + "OOBE")!;
        Assert.Equal("true", Value(oobe, "HideEULAPage"));
        Assert.Equal("true", Value(oobe, "HideOEMRegistrationScreen"));
        Assert.Equal("3", Value(oobe, "ProtectYourPC"));
        Assert.Equal("true", Value(oobe, "HideOnlineAccountScreens"));
        Assert.Equal("true", Value(oobe, "HideWirelessSetupInOOBE"));
    }

    [Fact]
    public void Oobe_microsoft_account_keeps_online_account_and_wireless_pages()
    {
        var oobe = Component(Parse(Setup(SetupAccount.Microsoft)), "oobeSystem", "Microsoft-Windows-Shell-Setup")
            .Element(Ns + "OOBE")!;
        Assert.Equal("true", Value(oobe, "HideEULAPage"));
        Assert.Equal("3", Value(oobe, "ProtectYourPC"));
        Assert.Null(oobe.Element(Ns + "HideOnlineAccountScreens"));
        Assert.Null(oobe.Element(Ns + "HideWirelessSetupInOOBE"));
    }

    [Fact]
    public void Oobe_sets_international_core()
    {
        var intl = Component(Parse(Setup()), "oobeSystem", "Microsoft-Windows-International-Core");
        Assert.Equal("pt-BR", Value(intl, "UILanguage"));
        Assert.Equal("0416:00010416", Value(intl, "InputLocale"));
    }

    [Fact]
    public void Profile_adds_first_logon_command()
    {
        var shell = Component(Parse(Setup(), withProfile: true), "oobeSystem", "Microsoft-Windows-Shell-Setup");
        var command = shell.Element(Ns + "FirstLogonCommands")!.Element(Ns + "SynchronousCommand")!;
        Assert.Equal("add", (string?)command.Attribute(XNamespace.Get("http://schemas.microsoft.com/WMIConfig/2002/State") + "action"));
        Assert.Equal("1", Value(command, "Order"));
        Assert.Equal(UnattendBuilder.FirstLogonCommand, Value(command, "CommandLine"));
        Assert.Equal("FormatKit", Value(command, "Description"));
    }

    [Fact]
    public void No_profile_means_no_first_logon_commands()
    {
        var shell = Component(Parse(Setup(), withProfile: false), "oobeSystem", "Microsoft-Windows-Shell-Setup");
        Assert.Null(shell.Element(Ns + "FirstLogonCommands"));
    }

    [Fact]
    public void Never_contains_secrets_or_disk_configuration()
    {
        string[] forbidden = ["Password", "AutoLogon", "UserAccounts", "DiskConfiguration", "InstallTo"];
        foreach (var account in Enum.GetValues<SetupAccount>())
        foreach (var edition in new string?[] { null, "Windows 11 Pro" })
        foreach (var profile in new[] { false, true })
        {
            var text = UnattendBuilder.Build(Setup(account, edition, "FK-PC"), profile);
            foreach (var word in forbidden)
                Assert.DoesNotContain(word, text);
        }
    }

    [Fact]
    public void Special_characters_are_escaped()
    {
        var text = UnattendBuilder.Build(Setup(edition: "Win & <Pro>", computerName: "A&B<"), withProfile: true);
        var root = XDocument.Parse(text).Root!;
        Assert.Equal("A&B<", Value(Component(root, "specialize", "Microsoft-Windows-Shell-Setup"), "ComputerName"));
        var meta = Component(root, "windowsPE", "Microsoft-Windows-Setup")
            .Descendants(Ns + "MetaData").Single();
        Assert.Equal("Win & <Pro>", Value(meta, "Value"));
    }

    [Fact]
    public void Declares_utf8()
    {
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", UnattendBuilder.Build(Setup(), false));
    }
}
