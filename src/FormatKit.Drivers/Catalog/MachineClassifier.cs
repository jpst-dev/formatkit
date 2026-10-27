using FormatKit.Core.Hardware;

namespace FormatKit.Drivers.Catalog;

public sealed record MachineKind(OemTool? Oem, BoardSupport? Board)
{
    public bool IsOem => Oem is not null;
}

public static class MachineClassifier
{
    public static MachineKind Classify(HardwareSnapshot hw, VendorCatalog catalog)
    {
        var oem = catalog.Oem.FirstOrDefault(o => Matches(o.ManufacturerMatches, hw.Machine.Manufacturer));
        if (oem is not null)
            return new MachineKind(oem, null);

        var board = catalog.Boards.FirstOrDefault(b => Matches(b.ManufacturerMatches, hw.Board.Manufacturer));
        return new MachineKind(null, board);
    }

    private static bool Matches(IReadOnlyList<string> prefixes, string? manufacturer)
    {
        var name = manufacturer?.Trim();
        if (string.IsNullOrEmpty(name))
            return false;
        return prefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}
