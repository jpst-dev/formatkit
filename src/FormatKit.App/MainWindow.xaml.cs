using System.Windows;
using FormatKit.App.ViewModels;

namespace FormatKit.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
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
