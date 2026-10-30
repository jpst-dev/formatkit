using FormatKit.Core;
using FormatKit.Core.Reporting;
using FormatKit.Core.Running;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Hosting;

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
    StartCountdown? Countdown = null)
{
    public HostEnvironment ToHost() => new(
        Services, Elevation, ScheduledTasks, Rebooter, Reports, StateStore, Log,
        RebootCountdown, NetworkRetryDelay, SimulateRebootAfterStep, Dev);

    public static CliEnvironment FromHost(HostEnvironment host, TextWriter output) => new(
        host.Services, host.Elevation, host.ScheduledTasks, host.Rebooter, host.Reports, host.StateStore, host.Log,
        output, host.RebootCountdown, host.NetworkRetryDelay, host.SimulateRebootAfterStep, host.Dev);
}
