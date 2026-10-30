using FormatKit.Media.Setup;

namespace FormatKit.Media.Tests.Setup;

public class GenericKeysTests
{
    /// <summary>Pins every entry against ruling R8 of the plan (Microsoft's public generic installation keys).</summary>
    [Theory]
    [InlineData("Windows 11 Home", "YTMG3-N6DKC-DKB77-7M9GH-8HVX7")]
    [InlineData("Windows 11 Home N", "4CPRK-NM3K3-X6XXQ-RXX86-WXCHW")]
    [InlineData("Windows 11 Home Single Language", "BT79Q-G7N6G-PGBYW-4YWX6-6F4BT")]
    [InlineData("Windows 11 Pro", "VK7JG-NPHTM-C97JM-9MPGT-3V66T")]
    [InlineData("Windows 11 Pro N", "2B87N-8KFHP-DKV6R-Y2C8J-PKCKT")]
    [InlineData("Windows 11 Pro for Workstations", "DXG7C-N36C4-C4HTG-X4T3X-2YV77")]
    [InlineData("Windows 11 Pro for Workstations N", "WYPNQ-8C467-V2W6J-TX4WX-WT2RQ")]
    [InlineData("Windows 11 Pro Education", "8PTT6-RNW4C-6V7J2-C2D3X-MHBPB")]
    [InlineData("Windows 11 Education", "YNMGQ-8RYV3-4PGQ3-C8XTP-7CFBY")]
    [InlineData("Windows 11 Education N", "84NGF-MHBT6-FXBX8-QWJK7-DRR8H")]
    [InlineData("Windows 11 Enterprise", "XGVPP-NMH47-7TTHJ-W3FW7-8HV2C")]
    [InlineData("Windows 11 Enterprise N", "WGGHN-J84D6-QYCPR-T7PJ7-X766F")]
    public void EveryKeyMatchesRulingR8(string edition, string key)
    {
        Assert.Equal(key, GenericKeys.For(edition));
        Assert.Contains(edition, GenericKeys.Editions);
    }

    [Fact]
    public void ProKey() => Assert.Equal("VK7JG-NPHTM-C97JM-9MPGT-3V66T", GenericKeys.For("Windows 11 Pro"));

    [Fact]
    public void LookupIsCaseInsensitive() => Assert.Equal("VK7JG-NPHTM-C97JM-9MPGT-3V66T", GenericKeys.For("windows 11 pro"));

    [Fact]
    public void UnknownEditionIsNull() => Assert.Null(GenericKeys.For("Windows 11 Ultimate"));

    [Fact]
    public void ListsAllTwelveEditions() => Assert.Equal(12, GenericKeys.Editions.Count);
}
