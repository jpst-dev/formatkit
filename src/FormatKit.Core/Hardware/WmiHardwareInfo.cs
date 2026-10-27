using System.Management;

namespace FormatKit.Core.Hardware;

public sealed class WmiHardwareInfo : IHardwareInfo
{
    public Task<HardwareSnapshot> ReadAsync(CancellationToken ct) => Task.Run(() => Read(ct), ct);

    private static HardwareSnapshot Read(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var system = Query("SELECT Manufacturer, Model, SystemFamily FROM Win32_ComputerSystem").FirstOrDefault();
        var machine = new MachineInfo(Str(system, "Manufacturer"), Str(system, "Model"), Str(system, "SystemFamily"));

        ct.ThrowIfCancellationRequested();
        var baseBoard = Query("SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();
        var board = new BoardInfo(Str(baseBoard, "Manufacturer"), Str(baseBoard, "Product"));

        ct.ThrowIfCancellationRequested();
        var bios = Str(Query("SELECT SMBIOSBIOSVersion FROM Win32_BIOS").FirstOrDefault(), "SMBIOSBIOSVersion");

        ct.ThrowIfCancellationRequested();
        var gpus = Query("SELECT Name, PNPDeviceID, DriverVersion FROM Win32_VideoController")
            .Select(o =>
            {
                var pnp = Str(o, "PNPDeviceID");
                return new GpuInfo(Str(o, "Name"), pnp, GpuVendors.FromPnpId(pnp), NullIfEmpty(Str(o, "DriverVersion")));
            })
            .ToList();

        ct.ThrowIfCancellationRequested();
        var problems = Query("SELECT Name, DeviceID, ConfigManagerErrorCode, HardwareID FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0 AND ConfigManagerErrorCode <> 22")
            .Select(o => new ProblemDevice(
                Str(o, "Name"),
                Str(o, "DeviceID"),
                o["ConfigManagerErrorCode"] is null ? 0 : Convert.ToInt32(o["ConfigManagerErrorCode"]),
                o["HardwareID"] is string[] ids ? ids : []))
            .ToList();

        ct.ThrowIfCancellationRequested();
        var signedDrivers = Query("SELECT DeviceID, InfName, DriverVersion FROM Win32_PnPSignedDriver")
            .GroupBy(o => Str(o, "DeviceID"), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        ct.ThrowIfCancellationRequested();
        var adapters = Query("SELECT Name, PNPDeviceID FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE")
            .Select(o =>
            {
                var pnp = Str(o, "PNPDeviceID");
                signedDrivers.TryGetValue(pnp, out var drv);
                return new NetworkAdapterInfo(
                    Str(o, "Name"),
                    pnp,
                    drv is null ? null : NullIfEmpty(Str(drv, "InfName")),
                    drv is null ? null : NullIfEmpty(Str(drv, "DriverVersion")));
            })
            .ToList();

        return new HardwareSnapshot(machine, board, bios, gpus, problems, adapters);
    }

    private static List<ManagementBaseObject> Query(string wql)
    {
        using var searcher = new ManagementObjectSearcher(wql);
        using var collection = searcher.Get();
        var list = new List<ManagementBaseObject>();
        foreach (var o in collection) list.Add(o);
        return list;
    }

    private static string Str(ManagementBaseObject? o, string property) => o?[property]?.ToString() ?? "";

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
}
