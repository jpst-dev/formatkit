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

    /// <summary>
    /// No <c>scheduledTask</c> action may target these (spec 5.3). An entry ending in <c>\</c> is a folder prefix
    /// (everything under it); any other entry is an exact task path (folder plus name).
    /// </summary>
    public static IReadOnlySet<string> ScheduledTasks { get; } = Set(
        @"\Microsoft\Windows\WindowsUpdate\",
        @"\Microsoft\Windows\UpdateOrchestrator\",
        @"\Microsoft\Windows\WaaSMedic\",
        @"\Microsoft\Windows\Defrag\ScheduledDefrag",
        @"\Microsoft\Windows\PI\Secure-Boot-Update",
        @"\Microsoft\Windows\Autochk\Proxy",
        @"\Microsoft\XblGameSave\XblGameSaveTask",
        @"\Microsoft\Windows\Windows Defender\",
        @"\Microsoft\Windows\Windows Error Reporting\QueueReporting");

    /// <summary>True when the task at <paramref name="path"/> + <paramref name="name"/> is protected.</summary>
    public static bool IsProtectedTask(string path, string name)
    {
        var full = path + name;
        return ScheduledTasks.Any(entry => entry.EndsWith('\\')
            ? full.StartsWith(entry, StringComparison.OrdinalIgnoreCase)
            : string.Equals(full, entry, StringComparison.OrdinalIgnoreCase));
    }

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
