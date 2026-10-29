using System.IO;
using System.Windows;
using System.Windows.Threading;
using FormatKit.App.Localization;
using FormatKit.App.Services;
using FormatKit.App.ViewModels;
using FormatKit.App.Worker;
using FormatKit.Core;
using FormatKit.Core.Logging;
using FormatKit.Core.SystemAccess;
using FormatKit.Hosting;
using FormatKit.Prepare;

namespace FormatKit.App;

public partial class App : Application
{
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
        StartupGuard.Run(() => StartWindow(e, arguments), ReportFatal, Shutdown);
    }

    private void StartWindow(StartupEventArgs e, AppArguments arguments)
    {
        _instance = SingleInstance.TryAcquire(InstanceName);
        if (_instance is null)
        {
            // Lets the first process take the foreground, which this one does not own. A --resume start (the resume
            // task after a reboot) asks the open window to resume the run, which would otherwise never happen.
            NativeMethods.AllowSetForegroundWindow(NativeMethods.AsfwAny);
            SingleInstance.SignalExisting(InstanceName, resume: arguments.Mode == AppMode.Resume);
            Shutdown(0);
            return;
        }

        _host = Hosting.Services.CreateReal(verbose: false, new HostOptions(["--resume"], ConsoleLog: false));
        if (e.Args.Length > 0 && arguments.Mode == AppMode.Window)
            _host.Log.Warning("Unrecognized arguments, opening the window: {Arguments}", e.Args);

        var set = ModuleSet.CreateReal(_host.Services, _host.Log, _host.NetworkRetryDelay, _host.Dev);
        var runner = new PrepareRunner(_host, set.Modules, PrepareChecks.All, PrepareEnvironment.Current);
        var launcher = new WorkerLauncher(new ElevationStarter(), _host.Log);
        var main = new MainViewModel(
            new HomeViewModel(_host.StateStore),
            new AboutViewModel(_dialogs),
            new HardwareViewModel(_host.Services, set.Vendors, _host.Log),
            new CleanupDiagnosisViewModel(_host.Services, set.Issues, _host.Log),
            new ChecklistViewModel(runner, new ProfileService(set.Modules), _dialogs, _host.Log),
            new CatalogsViewModel(set.Cleanup, set.Programs),
            new RunProfileViewModel(
                new ProfileService(set.Modules), set.Modules, launcher, _host.Elevation, _dialogs,
                new AppSettings(_host.Services.Files, Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FormatKit", "app.json")),
                _host));
        _window = new MainWindow(main);
        _instance.Activated += (_, _) => Dispatcher.BeginInvoke(() => _window?.BringToFront());
        _instance.ResumeRequested += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _window?.BringToFront();
            _ = main.ResumeAsync();
        });
        _window.Show();
        if (arguments.Mode == AppMode.Resume)
        {
            // Started by the resume scheduled task after a reboot: open on Run profile and continue the run.
            _ = main.ResumeAsync();
        }
    }

    /// <summary>
    /// Runs the worker's job and always shuts down with its exit code. The logger exists before anything can fail, so
    /// every failure path writes a log line.
    /// </summary>
    private async Task RunWorkerAsync(AppArguments arguments)
    {
        var code = WorkerHost.ExitFailed;
        HostEnvironment? host = null;
        try
        {
            host = Hosting.Services.CreateReal(verbose: false, new HostOptions(
                ["--resume"],
                ConsoleLog: false,
                Console: (session, log) => new NewConsoleInteractiveConsole(session, log)));
            var modules = ModuleSet.CreateReal(host.Services, host.Log, host.NetworkRetryDelay, host.Dev).Modules;
            code = await Task.Run(() => WorkerProcess.RunAsync(
                host, modules, arguments.PipeName!, arguments.ClientProcessId!.Value, arguments.Job,
                WorkerPipe.AcceptTimeout, CancellationToken.None));
        }
        catch (Exception ex)
        {
            LogStartupFailure(host, ex);
        }
        finally
        {
            try
            {
                (host?.Log as IDisposable)?.Dispose();
            }
            catch (Exception)
            {
                // The log is the only place left to report to; never let it keep the process alive.
            }

            Shutdown(code);
        }
    }

    /// <summary>Logs through the host, or, when building the host failed, through a logger of its own.</summary>
    private static void LogStartupFailure(HostEnvironment? host, Exception ex)
    {
        try
        {
            if (host is not null)
            {
                host.Log.Fatal(ex, "Worker: unexpected error");
                return;
            }

            var log = LogSetup.Create(AppPaths.Default, TimeProvider.System, verbose: false, console: false);
            log.Fatal(ex, "Worker: could not build the host");
            (log as IDisposable)?.Dispose();
        }
        catch (Exception)
        {
            // Not even a log file can be written.
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
        e.Handled = true;
        if (StartupGuard.ShutsDownAfterUnhandled(_window?.IsLoaded == true))
        {
            // No usable window (a XAML or startup failure): never stay alive hidden, holding the instance mutex.
            ReportFatal(e.Exception);
            Shutdown(1);
            return;
        }

        _host?.Log.Error(e.Exception, "Unhandled exception");
        _dialogs.ShowError(Strings.Format("Error_Unhandled", e.Exception.Message));
    }

    /// <summary>Logs a failure that ends the app (through a logger of its own when there is no host) and shows it.</summary>
    private void ReportFatal(Exception ex)
    {
        try
        {
            if (_host is not null)
            {
                _host.Log.Fatal(ex, "Window: startup failed");
            }
            else
            {
                var log = LogSetup.Create(AppPaths.Default, TimeProvider.System, verbose: false, console: false);
                log.Fatal(ex, "Window: startup failed before the host was built");
                (log as IDisposable)?.Dispose();
            }
        }
        catch (Exception)
        {
            // Not even a log file can be written; the dialog below still tells the user.
        }

        _dialogs.ShowError(Strings.Format("Error_Unhandled", ex.Message));
    }
}
