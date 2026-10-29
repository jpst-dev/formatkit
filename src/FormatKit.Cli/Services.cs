using FormatKit.Core;
using FormatKit.Core.Hardware;
using FormatKit.Core.Logging;
using FormatKit.Core.Reporting;
using FormatKit.Core.Running;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.WindowsUpdate;

namespace FormatKit.Cli;

public sealed record CliEnvironment(
    ServiceSet Services,
    IElevation Elevation,
    IScheduledTasks ScheduledTasks,
    IRebooter Rebooter,
    IReportWriter Reports,
    RunStateStore StateStore,
    Serilog.ILogger Log,
    TextWriter Out,
    TimeSpan RebootCountdown,
    TimeSpan NetworkRetryDelay,
    string? SimulateRebootAfterStep = null,
    FormatKit.Dev.DevEnvironment? Dev = null,
    StartCountdown? Countdown = null);

/// <summary>The composition root: the only place that builds real adapters.</summary>
public static class Services
{
    public static CliEnvironment CreateReal(bool verbose)
    {
        var paths = AppPaths.Default;
        var log = LogSetup.Create(paths, TimeProvider.System, verbose);
        var files = new FileSystem();
        var processes = new ProcessRunner(log);

        var registry = new WindowsRegistry();
        var userSession = new WindowsUserSession();
        var services = new ServiceSet(
            processes,
            files,
            new WmiHardwareInfo(),
            new ComWindowsUpdateClient(),
            new HttpNetworkProbe(),
            new WingetClient(processes),
            new PnpUtil(processes),
            registry,
            new ServiceControl(registry, processes),
            new PowerCfg(processes),
            new PowerShellAppxPackages(processes),
            new PowerShellOptionalFeatures(processes),
            userSession,
            new DirectInstaller(new HttpDownloader(), processes, files, paths),
            new ScheduledTaskUserContextRunner(processes),
            new WindowsInteractiveConsole(userSession, log),
            new RegistryUserEnvironment(registry),
            new ToolLocator(
                files,
                name => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine),
                name => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)),
            new WslClient(processes),
            new PowerShellVirtualDisks(processes, files, paths),
            new PowerShellVolumes(processes),
            new CimLicensing(processes),
            new PowerShellDiskImages(processes),
            new PowerShellWindowsImages(processes),
            new PowerShellDisks(processes),
            new PowerShellWindowsReadiness(processes));

        return new CliEnvironment(
            services,
            new Elevation(),
            new SchtasksScheduledTasks(processes, Environment.ProcessPath!),
            new ConsoleRebooter(processes, log),
            new ReportWriter(files, paths, TimeProvider.System),
            new RunStateStore(files, paths),
            log,
            Console.Out,
            RebootCountdown: TimeSpan.FromSeconds(60),
            NetworkRetryDelay: TimeSpan.FromSeconds(5),
            SimulateRebootAfterStep: Environment.GetEnvironmentVariable(Commands.SimulatedRebootStep.EnvironmentVariable));
    }
}
