using System.ComponentModel;
using System.Windows;
using FormatKit.App.ViewModels;

namespace FormatKit.App;

public partial class MainWindow : Window
{
    private readonly CloseGuard _closeGuard;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _closeGuard = new CloseGuard(() => viewModel.RunProfile.IsRunning, viewModel.RunProfile.ConfirmCloseAsync);
    }

    /// <summary>
    /// During a run the close waits: the page asks to stop after the current step, and the window closes once the run
    /// has ended. Close is posted, never called from inside this handler.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel && _closeGuard.ShouldCancel(() => Dispatcher.BeginInvoke(Close)))
            e.Cancel = true;
    }

    /// <summary>Brings the window to the front, for a second start of the app.</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        // The Topmost toggle makes Windows raise the window even when it is behind others.
        Topmost = true;
        Topmost = false;
        Activate();
    }
}
