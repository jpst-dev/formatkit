using System.IO;
using System.Windows;
using System.Windows.Threading;
using FormatKit.App.Localization;
using FormatKit.App.Services;
using FormatKit.App.ViewModels;
using FormatKit.App.Worker;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Hosting;
using FormatKit.Prepare;

namespace FormatKit.App;

public partial class App : Application
{
    /// <summary>Exit code of a worker that could not run its job (no window connected, or an unexpected error).</summary>
    private const int WorkerFailedExitCode = 2;

    private const string InstanceName = "Local\\FormatKit.App";

    private readonly IDialogService _dialogs = new WpfDialogService();
    private HostEnvironment? _host;
    private SingleInstance? _instance;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var arguments = AppArguments.Parse(e.Args);

        if (arguments.Mode == AppMode.Worker)
        {
            // No window and no instance mutex: the worker runs one job for the window that started it, then exits.
            _ = RunWorkerAsync(arguments);
            return;
        }

        // Before the host exists, so a failure while building it is reported too; the handler tolerates a null host.
        DispatcherUnhandledException += OnUnhandledException;

        _instance = SingleInstance.TryAcquire(InstanceName);
        if (_instance is null)
        {
            // Lets the first process take the foreground, which this one does not own.
            NativeMethods.AllowSetForegroundWindow(NativeMethods.AsfwAny);
            SingleInstance.SignalExisting(InstanceName);
            Shutdown(0);
            return;
        }

        _host = Hosting.Services.CreateReal(verbose: false, new HostOptions(["--resume"], ConsoleLog: false));
        if (e.Args.Length > 0 && arguments.Mode == AppMode.Window)
            _host.Log.Warning("Unrecognized arguments, opening the window: {Arguments}", e.Args);

        var set = ModuleSet.CreateReal(_host.Services, _host.Log, _host.NetworkRetryDelay, _host.Dev);
        var runner = new PrepareRunner(_host, set.Modules, PrepareChecks.All, PrepareEnvironment.Current);
        var main = new MainViewModel(
            new HomeViewModel(_host.StateStore),
            new AboutViewModel(_dialogs),
            new HardwareViewModel(_host.Services, set.Vendors, _host.Log),
            new CleanupDiagnosisViewModel(_host.Services, set.Issues, _host.Log),
            new ChecklistViewModel(runner, new ProfileService(set.Modules), _dialogs, _host.Log),
            new CatalogsViewModel(set.Cleanup, set.Programs));
        if (arguments.Mode == AppMode.Resume)
            main.Navigate(Page.RunProfile);

        _window = new MainWindow(main);
        _instance.Activated += (_, _) => Dispatcher.BeginInvoke(() => _window.BringToFront());
        _window.Show();
    }

    /// <summary>Accepts the window's connection, runs the job over it and shuts down with the job's exit code.</summary>
    private async Task RunWorkerAsync(AppArguments arguments)
    {
        var code = await Task.Run(() => RunWorkerJobAsync(arguments.PipeName!, arguments.Job));
        Shutdown(code);
    }

    private static async Task<int> RunWorkerJobAsync(string pipeName, IReadOnlyList<string> job)
    {
        Stream? stream = null;
        HostEnvironment? host = null;
        try
        {
            stream = await WorkerPipe.AcceptAsync(pipeName, WorkerPipe.AcceptTimeout, CancellationToken.None).ConfigureAwait(false);

            // The channel and rebooter are built inside the host's factory, so they log through the host's logger.
            MessageChannel? channel = null;
            PipeRebooter? rebooter = null;
            var connected = stream;
            Func<ServiceSet, Serilog.ILogger, IRebooter>? pipeRebooter = connected is null ? null : (services, log) =>
            {
                channel = new MessageChannel(connected, log);
                return rebooter = new PipeRebooter(channel, services.Processes, log);
            };
            host = Hosting.Services.CreateReal(verbose: false, new HostOptions(
                ["--resume"],
                ConsoleLog: false,
                Rebooter: pipeRebooter,
                Console: (session, log) => new NewConsoleInteractiveConsole(session, log)));

            if (channel is null || rebooter is null)
            {
                host.Log.Error("Worker: no window connected to {Pipe} within {Timeout}", pipeName, WorkerPipe.AcceptTimeout);
                return WorkerFailedExitCode;
            }

            await using (channel.ConfigureAwait(false))
            {
                stream = null; // owned by the channel now
                var set = ModuleSet.CreateReal(host.Services, host.Log, host.NetworkRetryDelay, host.Dev);
                var exitCode = await new WorkerHost(host, set.Modules, rebooter).RunAsync(channel, job, CancellationToken.None).ConfigureAwait(false);
                host.Log.Information("Worker: exiting with {ExitCode}", exitCode);
                return exitCode;
            }
        }
        catch (Exception ex)
        {
            host?.Log.Fatal(ex, "Worker: unexpected error");
            return WorkerFailedExitCode;
        }
        finally
        {
            if (stream is not null) await stream.DisposeAsync().ConfigureAwait(false);
            (host?.Log as IDisposable)?.Dispose();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        (_host?.Log as IDisposable)?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Log.Error(e.Exception, "Unhandled exception");
        _dialogs.ShowError(Strings.Format("Error_Unhandled", e.Exception.Message));
        e.Handled = true;
    }
}
