namespace FormatKit.Cleanup.Catalog;

/// <summary>Safety lists the catalog validator enforces (spec 5.4, 7.1). All sets are case-insensitive.</summary>
public static class ProtectedLists
{
    /// <summary>No <c>service</c> action may target these.</summary>
    public static IReadOnlySet<string> Services { get; } = Set(
        "W32Time", "wuauserv", "UsoSvc", "BITS", "CryptSvc", "TrustedInstaller", "defragsvc", "InstallService",
        "ClipSVC", "AppXSvc", "LicenseManager", "WinDefend", "SecurityHealthService", "mpssvc",
        "BFE", "Dnscache", "Dhcp", "NlaSvc", "DsmSvc", "DeviceInstall", "PlugPlay", "Power", "Schedule",
        "EventLog", "XboxGipSvc", "XblAuthManager", "XblGameSave", "ssh-agent", "IKEEXT",
        "SSDPSRV", "StiSvc", "SCardSvr", "SensorService", "vmcompute", "vmms", "LxssManager",
        "WSLService");

    /// <summary>Apps that are never removed. Entries ending in <c>*</c> are prefixes.</summary>
    public static IReadOnlySet<string> NeverRemoveApps { get; } = Set(
        "Microsoft.WindowsStore", "Microsoft.DesktopAppInstaller", "Microsoft.MicrosoftEdge*",
        "Microsoft.SecHealthUI", "Microsoft.XboxIdentityProvider", "Microsoft.Xbox.TCUI",
        "Microsoft.Windows.Photos", "Microsoft.WindowsCalculator", "Microsoft.WindowsNotepad",
        "Microsoft.WindowsTerminal", "Microsoft.ScreenSketch", "Microsoft.Paint",
        "Microsoft.VCLibs*", "Microsoft.UI.Xaml*", "Microsoft.NET.*");

    /// <summary>The only packages an <c>appx</c> action may remove.</summary>
    public static IReadOnlySet<string> RemovableApps { get; } = Set(
        "Clipchamp.Clipchamp", "Microsoft.MicrosoftSolitaireCollection", "Microsoft.BingNews",
        "Microsoft.BingWeather", "Microsoft.GetHelp", "Microsoft.Getstarted",
        "Microsoft.WindowsFeedbackHub", "Microsoft.PowerAutomateDesktop", "MSTeams",
        "Microsoft.GamingApp", "Microsoft.YourPhone", "Microsoft.Todos");

    /// <summary>Handler names a <c>named</c> action may use.</summary>
    public static IReadOnlySet<string> NamedHandlers { get; } = Set("memoryIntegrity");

    /// <summary>True when <paramref name="package"/> equals or is prefixed by a never-remove entry.</summary>
    internal static bool IsNeverRemove(string package) =>
        NeverRemoveApps.Any(entry => entry.EndsWith('*')
            ? package.StartsWith(entry[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(package, entry, StringComparison.OrdinalIgnoreCase));

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.OrdinalIgnoreCase);
}
