namespace FormatKit.Media.Setup;

/// <summary>Microsoft's public generic installation keys: they select an edition during setup and do not activate it.</summary>
public static class GenericKeys
{
    private static readonly Dictionary<string, string> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Windows 11 Home"] = "YTMG3-N6DKC-DKB77-7M9GH-8HVX7",
        ["Windows 11 Home N"] = "4CPRK-NM3K3-X6XXQ-RXX86-WXCHW",
        ["Windows 11 Home Single Language"] = "BT79Q-G7N6G-PGBYW-4YWX6-6F4BT",
        ["Windows 11 Pro"] = "VK7JG-NPHTM-C97JM-9MPGT-3V66T",
        ["Windows 11 Pro N"] = "2B87N-8KFHP-DKV6R-Y2C8J-PKCKT",
        ["Windows 11 Pro for Workstations"] = "DXG7C-N36C4-C4HTG-X4T3X-2YV77",
        ["Windows 11 Pro for Workstations N"] = "WYPNQ-8C467-V2W6J-TX4WX-WT2RQ",
        ["Windows 11 Pro Education"] = "8PTT6-RNW4C-6V7J2-C2D3X-MHBPB",
        ["Windows 11 Education"] = "YNMGQ-8RYV3-4PGQ3-C8XTP-7CFBY",
        ["Windows 11 Education N"] = "84NGF-MHBT6-FXBX8-QWJK7-DRR8H",
        ["Windows 11 Enterprise"] = "XGVPP-NMH47-7TTHJ-W3FW7-8HV2C",
        ["Windows 11 Enterprise N"] = "WGGHN-J84D6-QYCPR-T7PJ7-X766F",
    };

    public static IReadOnlyCollection<string> Editions => Keys.Keys;

    public static string? For(string editionName) => Keys.GetValueOrDefault(editionName);
}
