using FormatKit.App.Tests.Fakes;
using FormatKit.App.ViewModels;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Diagnosis;
using FormatKit.Core;
using FormatKit.Core.Running;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Drivers.Catalog;
using FormatKit.Hosting;
using FormatKit.Programs.Catalog;

namespace FormatKit.App.Tests;

internal static class TestViewModels
{
    public static RunStateStore Store(InMemoryFileSystem files) => new(files, new AppPaths(@"C:\FormatKitTest"));

    public static MainViewModel CreateMain(InMemoryFileSystem? files = null)
    {
        var store = Store(files ?? new InMemoryFileSystem());
        return new MainViewModel(
            new HomeViewModel(store),
            new AboutViewModel(new FakeDialogService()),
            new HardwareViewModel(TestServices.Create(), VendorCatalog.LoadEmbedded()),
            new CleanupDiagnosisViewModel(TestServices.Create(), new KnownIssues(1, [])),
            new ChecklistViewModel(
                new PrepareRunner(null!, [], () => [], () => null!), new ProfileService([]), new FakeDialogService()),
            new CatalogsViewModel(new CleanupCatalog(1, []), new ProgramCatalog(1, [])));
    }
}
