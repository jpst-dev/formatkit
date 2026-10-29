using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FormatKit.App.Localization;
using FormatKit.Core.Hardware;
using FormatKit.Core.Steps;
using FormatKit.Drivers;
using FormatKit.Drivers.Catalog;

namespace FormatKit.App.ViewModels;

/// <summary>The hardware cards and the driver scan, read-only (the CLI's <c>hardware</c> and <c>drivers scan</c>).</summary>
public sealed partial class HardwareViewModel : QueryPageViewModel
{
    private readonly ServiceSet _services;
    private readonly VendorCatalog _vendors;

    public HardwareViewModel(ServiceSet services, VendorCatalog vendors, Serilog.ILogger? log = null)
        : base(log)
    {
        _services = services;
        _vendors = vendors;
        LoadCommand = new AsyncRelayCommand(LoadAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        ScanCommand = new AsyncRelayCommand(ScanAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
    }

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand ScanCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MachineText), nameof(BoardText), nameof(BiosText), nameof(GpuLines),
        nameof(ProblemLines), nameof(AdapterLines))]
    private HardwareSnapshot? _hardware;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MachineTypeText), nameof(OnlineText), nameof(UpdateLines),
        nameof(GpuPackageLines), nameof(HasScan), nameof(HasWindowsUpdateError), nameof(WindowsUpdateErrorText))]
    private DriverScanReport? _scan;

    public bool HasScan => Scan is not null;

    public string MachineText => Hardware is { } hw ? $"{hw.Machine.Manufacturer} {hw.Machine.Model}".Trim() : string.Empty;

    public string BoardText => Hardware is { } hw ? $"{hw.Board.Manufacturer} {hw.Board.Product}".Trim() : string.Empty;

    public string BiosText => Hardware?.BiosVersion ?? string.Empty;

    public IReadOnlyList<string> GpuLines => OrNone(Hardware?.Gpus.Select(g =>
        Strings.Format("Hw_Gpu", g.Name, g.DriverVersion ?? Strings.Get("Hw_Unknown"))));

    public IReadOnlyList<string> ProblemLines => OrNone(Hardware?.ProblemDevices.Select(d =>
        Strings.Format("Hw_Problem", d.DisplayName, d.ErrorCode)));

    public IReadOnlyList<string> AdapterLines => OrNone(Hardware?.NetworkAdapters.Select(a =>
        Strings.Format("Hw_Adapter", a.Name, a.InfName ?? Strings.Get("Hw_Unknown"), a.DriverVersion ?? Strings.Get("Hw_Unknown"))));

    public string MachineTypeText => Scan is null
        ? string.Empty
        : Scan.Machine.Oem is { } oem
            ? Strings.Format("Scan_Oem", oem.Id)
            : Scan.Machine.Board is { } board
                ? Strings.Format("Scan_Board", board.SupportUrl)
                : Strings.Get("Scan_Custom");

    public string OnlineText => Scan is null ? string.Empty : Strings.Get(Scan.Online ? "Value_Yes" : "Value_No");

    public IReadOnlyList<string> UpdateLines => OrNone(Scan?.AvailableUpdates.Select(u => u.Title));

    public IReadOnlyList<string> GpuPackageLines => OrNone(Scan?.GpuPackages.Select(p => p.WingetId));

    public bool HasWindowsUpdateError => Scan?.WindowsUpdateError is not null;

    public string WindowsUpdateErrorText =>
        Scan?.WindowsUpdateError is { } error ? Strings.Format("Scan_WuError", error) : string.Empty;

    private Task LoadAsync() =>
        RunQueryAsync(ct => _services.Hardware.ReadAsync(ct), snapshot => Hardware = snapshot, nameof(LoadAsync));

    private Task ScanAsync() =>
        RunQueryAsync(ct => new DriverScanner(_services, _vendors).ScanAsync(ct), report => Scan = report, nameof(ScanAsync));

    private static IReadOnlyList<string> OrNone(IEnumerable<string>? items)
    {
        if (items is null)
            return [];
        var list = items.ToList();
        return list.Count == 0 ? [Strings.Get("Hw_None")] : list;
    }
}
