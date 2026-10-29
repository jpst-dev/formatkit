namespace FormatKit.Dev;

/// <summary>The machine facts profile validation and the dev steps depend on; tests build one by hand.</summary>
/// <param name="Expand">Expands <c>%VARIABLE%</c> references in a path.</param>
public sealed record DevEnvironment(
    string UserProfile, string Documents, string UserName, string ComputerName, int LogicalProcessors, Func<string, string> Expand)
{
    public static DevEnvironment Current() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.UserName,
        Environment.MachineName,
        Environment.ProcessorCount,
        Environment.ExpandEnvironmentVariables);
}
