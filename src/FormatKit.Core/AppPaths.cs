namespace FormatKit.Core;

public sealed record AppPaths(string Root)
{
    public static AppPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FormatKit"));

    public string StateFile => Path.Combine(Root, "state.json");
    public string LogsDirectory => Path.Combine(Root, "logs");
    public string ReportsDirectory => Path.Combine(Root, "reports");
}
