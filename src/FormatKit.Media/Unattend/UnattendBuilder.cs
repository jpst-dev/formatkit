using System.Xml.Linq;
using FormatKit.Media.Setup;

namespace FormatKit.Media.Unattend;

/// <summary>
/// Builds <c>autounattend.xml</c>. The file never holds a password, an account, auto-logon or disk
/// configuration: Setup asks for the disk and Windows asks for the account.
/// </summary>
public static class UnattendBuilder
{
    public const string FirstLogonCommand =
        "cmd /c start \"FormatKit\" \"%SystemDrive%\\FormatKit\\formatkit.exe\" run --profile \"%SystemDrive%\\FormatKit\\profile.json\" --countdown 60 --wait-on-exit";

    private static readonly XNamespace Ns = "urn:schemas-microsoft-com:unattend";
    private static readonly XNamespace Wcm = "http://schemas.microsoft.com/WMIConfig/2002/State";

    public static string Build(ResolvedSetup setup, bool withProfile)
    {
        var root = new XElement(Ns + "unattend",
            new XAttribute(XNamespace.Xmlns + "wcm", Wcm.NamespaceName),
            new XElement(Ns + "settings", new XAttribute("pass", "windowsPE"),
                WinPeInternational(setup),
                WindowsSetup(setup)),
            new XElement(Ns + "settings", new XAttribute("pass", "specialize"),
                Specialize(setup)),
            new XElement(Ns + "settings", new XAttribute("pass", "oobeSystem"),
                OobeInternational(setup),
                OobeShell(setup, withProfile)));

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" + root.ToString() + "\r\n";
    }

    private static XElement Component(string name, params object[] content) =>
        new(Ns + "component",
            new XAttribute("name", name),
            new XAttribute("processorArchitecture", "amd64"),
            new XAttribute("publicKeyToken", "31bf3856ad364e35"),
            new XAttribute("language", "neutral"),
            new XAttribute("versionScope", "nonSxS"),
            content);

    private static XElement El(string name, string value) => new(Ns + name, value);

    private static XElement WinPeInternational(ResolvedSetup s) =>
        Component("Microsoft-Windows-International-Core-WinPE",
            new XElement(Ns + "SetupUILanguage", El("UILanguage", s.Language)),
            El("InputLocale", s.Keyboard),
            El("SystemLocale", s.Language),
            El("UILanguage", s.Language),
            El("UserLocale", s.Language));

    private static XElement WindowsSetup(ResolvedSetup s)
    {
        var component = Component("Microsoft-Windows-Setup");

        if (s.Edition is not null)
        {
            component.Add(new XElement(Ns + "ImageInstall",
                new XElement(Ns + "OSImage",
                    new XElement(Ns + "InstallFrom",
                        new XElement(Ns + "MetaData", new XAttribute(Wcm + "action", "add"),
                            El("Key", "/IMAGE/NAME"),
                            El("Value", s.Edition))))));
        }

        var userData = new XElement(Ns + "UserData", El("AcceptEula", "true"));
        var key = s.Edition is null ? null : GenericKeys.For(s.Edition);
        if (key is not null)
            userData.Add(new XElement(Ns + "ProductKey", El("Key", key), El("WillShowUI", "OnError")));
        component.Add(userData);
        return component;
    }

    private static XElement Specialize(ResolvedSetup s)
    {
        var component = Component("Microsoft-Windows-Shell-Setup");
        if (s.ComputerName is not null) component.Add(El("ComputerName", s.ComputerName));
        component.Add(El("TimeZone", s.TimeZone));
        return component;
    }

    private static XElement OobeInternational(ResolvedSetup s) =>
        Component("Microsoft-Windows-International-Core",
            El("InputLocale", s.Keyboard),
            El("SystemLocale", s.Language),
            El("UILanguage", s.Language),
            El("UserLocale", s.Language));

    private static XElement OobeShell(ResolvedSetup s, bool withProfile)
    {
        var oobe = new XElement(Ns + "OOBE",
            El("HideEULAPage", "true"),
            El("HideOEMRegistrationScreen", "true"));
        if (s.Account == SetupAccount.Local)
        {
            oobe.Add(El("HideOnlineAccountScreens", "true"));
            oobe.Add(El("HideWirelessSetupInOOBE", "true"));
        }
        oobe.Add(El("ProtectYourPC", "3"));

        var component = Component("Microsoft-Windows-Shell-Setup", oobe);
        if (withProfile)
        {
            component.Add(new XElement(Ns + "FirstLogonCommands",
                new XElement(Ns + "SynchronousCommand", new XAttribute(Wcm + "action", "add"),
                    El("Order", "1"),
                    El("CommandLine", FirstLogonCommand),
                    El("Description", "FormatKit"))));
        }
        return component;
    }
}
