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
        InMemoryFileSystem? files = null)
    {
        processes ??= new FakeProcessRunner();
        return new ServiceSet(
            processes,
            files ?? new InMemoryFileSystem(),
            hardware ?? new FakeHardwareInfo(FakeHardwareInfo.Sample()),
            wu ?? new FakeWindowsUpdateClient(),
            network ?? new FakeNetworkProbe(true),
            new WingetClient(processes),
            new PnpUtil(processes));
    }

    public static StepContext Context(ServiceSet services) =>
        new(services, new LoggerConfiguration().CreateLogger());
}
