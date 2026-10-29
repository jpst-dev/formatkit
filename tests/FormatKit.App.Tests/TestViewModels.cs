using FormatKit.App.Services;
using FormatKit.App.Tests.Fakes;
using FormatKit.App.ViewModels;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Diagnosis;
using FormatKit.Core;
using FormatKit.Core.Running;
using FormatKit.Core.SystemAccess;
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
        files ??= new InMemoryFileSystem();
        var store = Store(files);
        return new MainViewModel(
            new HomeViewModel(store),
            new AboutViewModel(new FakeDialogService()),
            new HardwareViewModel(TestServices.Create(), VendorCatalog.LoadEmbedded()),
            new CleanupDiagnosisViewModel(TestServices.Create(), new KnownIssues(1, [])),
            new ChecklistViewModel(
                new PrepareRunner(null!, [], () => [], () => null!), new ProfileService([]), new FakeDialogService()),
            new CatalogsViewModel(new CleanupCatalog(1, []), new ProgramCatalog(1, [])),
            new RunProfileViewModel(
                new ProfileService([]), [], new NoLauncher(), new Elevation(), new FakeDialogService(),
                new AppSettings(files, @"C:\FormatKitTest\app.json"),
                new HostEnvironment(
                    TestServices.Create(files: files), new Elevation(), new FakeScheduledTasks(), new FakeRebooter(),
                    new FakeReportWriter(), store, new Serilog.LoggerConfiguration().CreateLogger(),
                    TimeSpan.Zero, TimeSpan.Zero)));
    }
}
