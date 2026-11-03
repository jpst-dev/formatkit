using System.IO.Enumeration;

namespace FormatKit.Programs.Capture;

/// <summary>Packages a capture leaves out: they arrive as dependencies or with Windows, so listing them only adds failures.</summary>
public static class SkippedPackages
{
    private static readonly string[] Patterns =
    [
        "Microsoft.VCRedist.*",
        "Microsoft.VCLibs*",
        "Microsoft.UI.Xaml*",
        "Microsoft.WindowsAppRuntime*",
        "Microsoft.DotNet.*Runtime*",
        "Microsoft.DotNet.AspNetCore*",
        "Microsoft.Edge",
        "Microsoft.EdgeWebView2Runtime",
        "Microsoft.AppInstaller",

        // What winget export listed on a clean Windows 11 (VM, build 26300). Windows brings these, and installing them
        // would work against a cleanup that removes them. WSL is the dev section's job.
        "Microsoft.OneDrive",
        "Microsoft.Teams",
        "Microsoft.365Copilot",
        "Microsoft.Outlook",
        "Microsoft.WindowsTerminal",
        "Microsoft.WSL",
    ];

    public static bool IsSkipped(string wingetId) =>
        Patterns.Any(p => FileSystemName.MatchesSimpleExpression(p, wingetId, ignoreCase: true));
}
