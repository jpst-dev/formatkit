using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using Serilog;

namespace FormatKit.Core.Tests.Fakes;

public static class TestServices
{
    public static ServiceSet Create(
        FakeProcessRunner? processes = null,
        FakeHardwareInfo? hardware = null,
        FakeWindowsUpdateClient? wu = null,
        FakeNetworkProbe? network = null,
        InMemoryFileSystem? files = null,
        FakeRegistry? registry = null,
        FakeServiceControl? services = null,
        FakePowerSchemes? power = null,
        FakeAppxPackages? appx = null,
        FakeOptionalFeatures? features = null,
        FakeUserSession? session = null,
        DirectInstaller? direct = null,
        IUserContextRunner? userContext = null)
    {
        processes ??= new FakeProcessRunner();
        files ??= new InMemoryFileSystem();
        return new ServiceSet(
            processes,
            files,
            hardware ?? new FakeHardwareInfo(FakeHardwareInfo.Sample()),
            wu ?? new FakeWindowsUpdateClient(),
            network ?? new FakeNetworkProbe(true),
            new WingetClient(processes),
            new PnpUtil(processes),
            registry ?? new FakeRegistry(),
            services ?? new FakeServiceControl(),
            power ?? new FakePowerSchemes(),
            appx ?? new FakeAppxPackages(),
            features ?? new FakeOptionalFeatures(),
            session ?? new FakeUserSession(),
            direct ?? new DirectInstaller(new FakeHttpDownloader(files), processes, files, new AppPaths(@"C:\FormatKitTest")),
            userContext ?? new FakeUserContextRunner());
    }

    public static StepContext Context(ServiceSet services) =>
        new(services, new LoggerConfiguration().CreateLogger());
}
