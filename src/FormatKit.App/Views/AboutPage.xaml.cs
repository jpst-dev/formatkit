using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace FormatKit.App.Views;

public partial class AboutPage : UserControl
{
    public AboutPage() => InitializeComponent();

    private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
