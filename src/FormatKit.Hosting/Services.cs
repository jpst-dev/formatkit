using FormatKit.Core;
using FormatKit.Core.Hardware;
using FormatKit.Core.Logging;
using FormatKit.Core.Reporting;
using FormatKit.Core.Running;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.WindowsUpdate;
using FormatKit.Dev;

namespace FormatKit.Hosting;

public sealed record HostEnvironment(
    ServiceSet Services,
    IElevation Elevation,
    IScheduledTasks ScheduledTasks,
    IRebooter Rebooter,
    IReportWriter Reports,
    RunStateStore StateStore,
    Serilog.ILogger Log,
    TimeSpan RebootCountdown,
    TimeSpan NetworkRetryDelay,
    string? SimulateRebootAfterStep = null,
    DevEnvironment? Dev = null);

/// <summary>
/// What differs between hosts. <paramref name="ResumeArguments"/> is the command line the resume scheduled task
/// passes to the executable; the factories default to the console rebooter and the Windows interactive console.
/// </summary>
public sealed record HostOptions(
    IReadOnlyList<string> ResumeArguments,
    bool ConsoleLog,
    Func<ServiceSet, Serilog.ILogger, IRebooter>? Rebooter = null,
    Func<IUserSession, Serilog.ILogger, IInteractiveConsole>? Console = null);

/// <summary>The composition root: the only place that builds real adapters.</summary>
public static class Services
{
    public static HostEnvironment CreateReal(bool verbose, HostOptions options)
    {
        var paths = AppPaths.Default;
        var log = LogSetup.Create(paths, TimeProvider.System, verbose, options.ConsoleLog);
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
            (options.Console ?? ((session, logger) => new WindowsInteractiveConsole(session, logger)))(userSession, log),
            new RegistryUserEnvironment(registry),
            new ToolLocator(
                files,
                name => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine),
                name => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)),
            new WslClient(processes),
            new PowerShellVirtualDisks(processes, files, paths),
            new PowerShellVolumes(processes),
            new CimLicensing(processes));

        return new HostEnvironment(
            services,
            new Elevation(),
            new SchtasksScheduledTasks(processes, Environment.ProcessPath!, options.ResumeArguments),
            (options.Rebooter ?? ((_, logger) => new ConsoleRebooter(processes, logger)))(services, log),
            new ReportWriter(files, paths, TimeProvider.System),
            new RunStateStore(files, paths),
            log,
            RebootCountdown: TimeSpan.FromSeconds(60),
            NetworkRetryDelay: TimeSpan.FromSeconds(5),
            SimulateRebootAfterStep: Environment.GetEnvironmentVariable(SimulatedRebootStep.EnvironmentVariable));
    }
}
