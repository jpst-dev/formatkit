using FormatKit.Core.Hardware;
using FormatKit.Core.Steps;
using FormatKit.Core.WindowsUpdate;
using FormatKit.Drivers.Catalog;

namespace FormatKit.Drivers;

public sealed record DriverScanReport(
    HardwareSnapshot Hardware,
    MachineKind Machine,
    IReadOnlyList<DriverUpdate> AvailableUpdates,
    IReadOnlyList<GpuPackage> GpuPackages,
    bool Online,
    string? WindowsUpdateError = null);

/// <summary>Read-only survey of what driver work FormatKit would do on this machine. Installs nothing.</summary>
public sealed class DriverScanner(ServiceSet services, VendorCatalog catalog)
{
    public async Task<DriverScanReport> ScanAsync(CancellationToken ct)
    {
        var hardware = await services.Hardware.ReadAsync(ct);
        var online = await services.Network.IsOnlineAsync(ct);

        IReadOnlyList<DriverUpdate> updates = [];
        string? updateError = null;
        try
        {
            updates = await services.WindowsUpdate.SearchDriversAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            updateError = ex.Message;
        }

        return new DriverScanReport(
            hardware,
            MachineClassifier.Classify(hardware, catalog),
            updates,
            GpuPackageSelector.Select(hardware, catalog),
            online,
            updateError);
    }
}
