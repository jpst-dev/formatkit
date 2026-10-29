using System.Runtime.ExceptionServices;
using System.Windows.Controls;

namespace FormatKit.App.Tests;

/// <summary>Every page's XAML parses, including its styles' StaticResource lookups; no window is shown.</summary>
public sealed class ViewsLoadTests
{
    public static TheoryData<string> Pages() =>
        new(typeof(App).Assembly.GetTypes()
            .Where(t => t.Namespace == "FormatKit.App.Views" && typeof(UserControl).IsAssignableFrom(t))
            .Select(t => t.FullName!));

    [Theory]
    [MemberData(nameof(Pages))]
    public void Page_LoadsItsXaml(string typeName) =>
        OnSta(() => Activator.CreateInstance(typeof(App).Assembly.GetType(typeName)!));

    [Fact]
    public void MainWindow_LoadsItsXaml() =>
        // Constructed, never shown: no window handle is created.
        OnSta(() => _ = new MainWindow(TestViewModels.CreateMain()));

    private static void OnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure?.Throw();
    }
}
