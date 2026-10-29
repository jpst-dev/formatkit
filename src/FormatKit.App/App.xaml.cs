using System.Windows;
using System.Windows.Threading;
using FormatKit.App.Localization;
using FormatKit.App.Services;
using FormatKit.App.ViewModels;
using FormatKit.Hosting;

namespace FormatKit.App;

public partial class App : Application
{
    /// <summary>Exit code of the worker mode until it is wired.</summary>
    private const int WorkerNotWiredExitCode = 2;

    private const string InstanceName = "Local\\FormatKit.App";

    private readonly IDialogService _dialogs = new WpfDialogService();
    private HostEnvironment? _host;
    private SingleInstance? _instance;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Before the host exists, so a failure while building it is reported too; the handler tolerates a null host.
        DispatcherUnhandledException += OnUnhandledException;
        var arguments = AppArguments.Parse(e.Args);

        if (arguments.Mode == AppMode.Worker)
        {
            // The worker wiring arrives with the elevated worker task; the worker does not take the instance mutex.
            Shutdown(WorkerNotWiredExitCode);
            return;
        }

        _instance = SingleInstance.TryAcquire(InstanceName);
        if (_instance is null)
        {
            SingleInstance.SignalExisting(InstanceName);
            Shutdown(0);
            return;
        }

        _host = Hosting.Services.CreateReal(verbose: false, new HostOptions(["--resume"], ConsoleLog: false));
        if (e.Args.Length > 0 && arguments.Mode == AppMode.Window)
            _host.Log.Warning("Unrecognized arguments, opening the window: {Arguments}", e.Args);

        var main = new MainViewModel(new HomeViewModel(_host.StateStore), new AboutViewModel(_dialogs));
        if (arguments.Mode == AppMode.Resume)
            main.Navigate(Page.RunProfile);

        _window = new MainWindow(main);
        _instance.Activated += (_, _) => Dispatcher.BeginInvoke(() => _window.BringToFront());
        _window.Show();
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
