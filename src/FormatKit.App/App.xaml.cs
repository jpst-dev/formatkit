using System.Windows;
using System.Windows.Threading;
using FormatKit.App.Localization;
using FormatKit.App.Services;
using FormatKit.Hosting;

namespace FormatKit.App;

public partial class App : Application
{
    /// <summary>Exit code of the worker mode until it is wired.</summary>
    private const int WorkerNotWiredExitCode = 2;

    private readonly IDialogService _dialogs = new WpfDialogService();
    private HostEnvironment? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var arguments = AppArguments.Parse(e.Args);

        if (arguments.Mode == AppMode.Worker)
        {
            // The worker wiring arrives with the elevated worker task.
            Shutdown(WorkerNotWiredExitCode);
            return;
        }

        _host = Hosting.Services.CreateReal(verbose: false, new HostOptions(["--resume"], ConsoleLog: false));
        DispatcherUnhandledException += OnUnhandledException;
        if (e.Args.Length > 0 && arguments.Mode == AppMode.Window)
            _host.Log.Warning("Unrecognized arguments, opening the window: {Arguments}", e.Args);

        // Window and Resume both show the main window for now.
        new MainWindow().Show();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Log.Error(e.Exception, "Unhandled exception");
        _dialogs.ShowError(Strings.Format("Error_Unhandled", e.Exception.Message));
        e.Handled = true;
    }
}
