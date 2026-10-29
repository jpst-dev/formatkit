namespace FormatKit.App;

public enum AppMode { Window, Resume, Worker }

/// <summary>The command line of FormatKit.exe: the window, the resume after a reboot, or an elevated worker.</summary>
public sealed record AppArguments(AppMode Mode, string? PipeName, IReadOnlyList<string> Job)
{
    private static readonly string[] None = [];

    public static AppArguments Parse(string[] args)
    {
        if (args.Length == 1 && args[0] == "--resume")
            return new AppArguments(AppMode.Resume, null, None);

        if (args.Length >= 3 && args[0] == "--worker" && args[1] == "--pipe")
            return new AppArguments(AppMode.Worker, args[2], args[3..]);

        return new AppArguments(AppMode.Window, null, None);
    }
}
