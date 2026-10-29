using FormatKit.Core.Hardware;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.WindowsUpdate;

namespace FormatKit.Core.Steps;

public sealed record ServiceSet(
    IProcessRunner Processes,
    IFileSystem Files,
    IHardwareInfo Hardware,
    IWindowsUpdateClient WindowsUpdate,
    INetworkProbe Network,
    WingetClient Winget,
    PnpUtil PnpUtil,
    IRegistry Registry,
    IServiceControl ServiceControl,
    IPowerSchemes Power,
    IAppxPackages Appx,
    IOptionalFeatures Features,
    IUserSession UserSession,
    DirectInstaller Direct,
    IUserContextRunner UserContext,
    IInteractiveConsole Console,
    IUserEnvironment UserEnvironment,
    IToolLocator Tools,
    WslClient Wsl,
    IVirtualDisks VirtualDisks,
    IVolumes Volumes,
    ILicensing Licensing);
