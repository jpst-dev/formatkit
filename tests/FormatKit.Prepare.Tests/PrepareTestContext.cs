using FormatKit.Core.Profiles;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using Serilog;

namespace FormatKit.Prepare.Tests;

/// <summary>Builds a <see cref="PrepareContext"/> for check tests: system drive C, user joao, not elevated, no profile.</summary>
public static class PrepareTestContext
{
    public const string UserProfile = @"C:\Users\joao";
    public const string LocalAppData = @"C:\Users\joao\AppData\Local";
    public const string AppData = @"C:\Users\joao\AppData\Roaming";
    public const string ProgramFiles = @"C:\Program Files";

    public static PrepareContext Create(
        ServiceSet? services = null,
        IReadOnlyList<VolumeInfo>? volumes = null,
        bool isElevated = false,
        LoadedProfile? profile = null,
        IReadOnlyList<ExternalInput>? inputs = null,
        ILogger? log = null)
    {
        var env = new PrepareEnvironment(UserProfile, LocalAppData, AppData, 'C', "PC-TEST", Expand);
        return new PrepareContext(
            services ?? TestServices.Create(),
            env,
            new WindowsVolume('C', volumes ?? []),
            profile,
            inputs ?? [],
            isElevated,
            log ?? new LoggerConfiguration().CreateLogger());
    }

    private static string Expand(string text) => text
        .Replace("%USERPROFILE%", UserProfile, StringComparison.OrdinalIgnoreCase)
        .Replace("%LOCALAPPDATA%", LocalAppData, StringComparison.OrdinalIgnoreCase)
        .Replace("%APPDATA%", AppData, StringComparison.OrdinalIgnoreCase)
        .Replace("%ProgramFiles%", ProgramFiles, StringComparison.OrdinalIgnoreCase);
}
