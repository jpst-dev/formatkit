namespace FormatKit.Core.SystemAccess;

public sealed class ServiceControl(IRegistry registry, IProcessRunner runner) : IServiceControl
{
    private const string ServicesPath = "HKLM\\SYSTEM\\CurrentControlSet\\Services\\";

    public ServiceStartType? GetStartType(string name)
    {
        var value = registry.Read(ServicesPath + name, "Start");
        if (value is null || !uint.TryParse(value.Data, out var start))
        {
            return null;
        }

        // 0 (boot) and 1 (system) are driver start types; both start automatically.
        return start switch
        {
            0 or 1 or 2 => ServiceStartType.Automatic,
            3 => ServiceStartType.Manual,
            4 => ServiceStartType.Disabled,
            _ => null,
        };
    }

    public async Task SetStartTypeAsync(string name, ServiceStartType type, CancellationToken ct)
    {
        var value = type switch
        {
            ServiceStartType.Automatic => "auto",
            ServiceStartType.Manual => "demand",
            ServiceStartType.Disabled => "disabled",
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

        var result = await runner.RunAsync(
            new ProcessSpec("sc.exe", ["config", name, "start=", value], Timeouts.Query),
            ct).ConfigureAwait(false);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException(
                $"Failed to set service '{name}' start type (exit {result.ExitCode}).");
        }
    }
}
