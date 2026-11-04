using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace FormatKit.App.Views;

public partial class HomePage : UserControl
{
    public HomePage() => InitializeComponent();

    private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
