using FormatKit.Core.Profiles;
using FormatKit.Core.Steps;

namespace FormatKit.Prepare;

public sealed record PrepareEnvironment(
    string UserProfile,
    string LocalAppData,
    string AppData,
    char SystemDrive,
    string ComputerName,
    Func<string, string> Expand)
{
    public static PrepareEnvironment Current()
    {
        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
        return new PrepareEnvironment(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            string.IsNullOrEmpty(systemDrive) ? 'C' : char.ToUpperInvariant(systemDrive[0]),
            Environment.MachineName,
            Environment.ExpandEnvironmentVariables);
    }
}

public sealed record PrepareContext(
    ServiceSet Services,
    PrepareEnvironment Env,
    WindowsVolume Windows,
    LoadedProfile? Profile,
    IReadOnlyList<ExternalInput> Inputs,
    bool IsElevated,
    Serilog.ILogger Log);
