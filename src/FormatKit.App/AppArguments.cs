using System.Globalization;

namespace FormatKit.App;

public enum AppMode { Window, Resume, Worker }

/// <summary>
/// The command line of FormatKit.exe: the window, the resume after a reboot, or an elevated worker
/// (<c>--worker --pipe &lt;name&gt; --client-pid &lt;window pid&gt; &lt;job...&gt;</c>).
/// </summary>
public sealed record AppArguments(AppMode Mode, string? PipeName, IReadOnlyList<string> Job, int? ClientProcessId = null)
{
    private static readonly string[] None = [];

    public static AppArguments Parse(string[] args)
    {
        if (args.Length == 1 && args[0] == "--resume")
            return new AppArguments(AppMode.Resume, null, None);

        if (args.Length >= 5 && args[0] == "--worker" && args[1] == "--pipe" && args[3] == "--client-pid"
            && int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out var clientPid) && clientPid > 0)
            return new AppArguments(AppMode.Worker, args[2], args[5..], clientPid);

        return new AppArguments(AppMode.Window, null, None);
    }
}
