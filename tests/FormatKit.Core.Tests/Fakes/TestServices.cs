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
        IUserContextRunner? userContext = null,
        FakeInteractiveConsole? console = null,
        FakeUserEnvironment? userEnvironment = null,
        FakeToolLocator? tools = null,
        FakeVirtualDisks? virtualDisks = null,
        FakeVolumes? volumes = null,
        FakeLicensing? licensing = null,
        FakeDiskImages? diskImages = null,
        FakeWindowsImages? windowsImages = null,
        FakeDisks? disks = null)
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
            userContext ?? new FakeUserContextRunner(),
            console ?? new FakeInteractiveConsole(),
            userEnvironment ?? new FakeUserEnvironment(),
            tools ?? new FakeToolLocator(),
            new WslClient(processes),
            virtualDisks ?? new FakeVirtualDisks(),
            volumes ?? new FakeVolumes(new VolumeInfo('C', 0, BitLockerState.Off, null, null)),
            licensing ?? new FakeLicensing(new WindowsActivation(true, "OEM:DM")),
            diskImages ?? new FakeDiskImages(),
            windowsImages ?? new FakeWindowsImages(files),
            disks ?? new FakeDisks());
    }

    public static StepContext Context(ServiceSet services) =>
        new(services, new LoggerConfiguration().CreateLogger());
}
