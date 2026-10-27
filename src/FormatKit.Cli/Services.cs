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
    TimeSpan NetworkRetryDelay);

/// <summary>The composition root: the only place that builds real adapters.</summary>
public static class Services
{
    public static CliEnvironment CreateReal(bool verbose)
    {
        var paths = AppPaths.Default;
        var log = LogSetup.Create(paths, TimeProvider.System, verbose);
        var files = new FileSystem();
        var processes = new ProcessRunner(log);

        var services = new ServiceSet(
            processes,
            files,
            new WmiHardwareInfo(),
            new ComWindowsUpdateClient(),
            new HttpNetworkProbe(),
            new WingetClient(processes),
            new PnpUtil(processes));

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
            NetworkRetryDelay: TimeSpan.FromSeconds(5));
    }
}
