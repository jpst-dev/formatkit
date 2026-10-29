using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FormatKit.App.Localization;
using Microsoft.Win32;

namespace FormatKit.App.Services;

public sealed class WpfDialogService : IDialogService
{
    public string? OpenFile(string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveFile(string filter, string defaultName)
    {
        var dialog = new SaveFileDialog { Filter = filter, FileName = defaultName };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowError(string message) =>
        MessageBox.Show(message, Strings.Get("App_Title"), MessageBoxButton.OK, MessageBoxImage.Error);

    public void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

    public Task<bool> RebootCountdownAsync(int seconds, CancellationToken ct)
    {
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var remaining = seconds;

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        var restart = new Button { Content = Strings.Get("Reboot_Now"), MinWidth = 100, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = Strings.Get("Reboot_Cancel"), MinWidth = 100, IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(restart);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(text);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Title = Strings.Get("Reboot_Title"),
            Content = panel,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true,
        };

        void Render() => text.Text = Strings.Format("Reboot_Countdown", remaining);
        Render();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

        void Finish(bool value)
        {
            timer.Stop();
            result.TrySetResult(value);
            if (window.IsVisible) window.Close();
        }

        timer.Tick += (_, _) =>
        {
            remaining--;
            if (remaining <= 0) Finish(true);
            else Render();
        };
        restart.Click += (_, _) => Finish(true);
        cancel.Click += (_, _) => Finish(false);
        window.Closed += (_, _) => Finish(false);
        ct.Register(() => window.Dispatcher.BeginInvoke(() => Finish(false)));

        timer.Start();
        window.Show();
        return result.Task;
    }
}
