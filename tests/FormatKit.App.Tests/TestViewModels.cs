using FormatKit.App.Tests.Fakes;
using FormatKit.App.ViewModels;
using FormatKit.Core;
using FormatKit.Core.Running;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.App.Tests;

internal static class TestViewModels
{
    public static RunStateStore Store(InMemoryFileSystem files) => new(files, new AppPaths(@"C:\FormatKitTest"));

    public static MainViewModel CreateMain(InMemoryFileSystem? files = null)
    {
        var store = Store(files ?? new InMemoryFileSystem());
        return new MainViewModel(new HomeViewModel(store), new AboutViewModel(new FakeDialogService()));
    }
}
