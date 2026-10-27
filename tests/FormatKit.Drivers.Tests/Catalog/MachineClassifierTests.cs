using FormatKit.Core.Tests.Fakes;
using FormatKit.Drivers.Catalog;

namespace FormatKit.Drivers.Tests.Catalog;

public class MachineClassifierTests
{
    private static readonly VendorCatalog Catalog = VendorCatalog.LoadEmbedded();

    private static MachineKind Classify(string manufacturer, string board = "")
        => MachineClassifier.Classify(FakeHardwareInfo.Sample(manufacturer: manufacturer, boardManufacturer: board), Catalog);

    [Fact]
    public void Dell_is_oem()
    {
        var kind = Classify("Dell Inc.");
        Assert.True(kind.IsOem);
        Assert.Equal("dell", kind.Oem!.Id);
    }

    [Fact]
    public void Matching_ignores_case_and_whitespace() => Assert.Equal("lenovo", Classify("  lenovo ").Oem!.Id);

    [Theory]
    [InlineData("HP")]
    [InlineData("Hewlett-Packard")]
    [InlineData("HP Inc.")]
    public void Hp_names_match_hp(string manufacturer) => Assert.Equal("hp", Classify(manufacturer).Oem!.Id);

    [Fact]
    public void Oem_wins_over_board()
    {
        var kind = Classify("Dell Inc.", "ASUSTeK COMPUTER INC.");
        Assert.Equal("dell", kind.Oem!.Id);
        Assert.Null(kind.Board);
    }

    [Theory]
    [InlineData("ASUSTeK COMPUTER INC.", "https://www.asus.com/support/")]
    [InlineData("Micro-Star International Co., Ltd.", "https://www.msi.com/support")]
    [InlineData("Gigabyte Technology Co., Ltd.", "https://www.gigabyte.com/Support")]
    [InlineData("ASRock", "https://www.asrock.com/support/index.asp")]
    public void Custom_build_matches_board(string board, string url)
    {
        var kind = Classify("To Be Filled By O.E.M.", board);
        Assert.False(kind.IsOem);
        Assert.Equal(url, kind.Board!.SupportUrl);
    }

    [Fact]
    public void Empty_manufacturer_is_custom()
    {
        var kind = Classify("", "");
        Assert.Null(kind.Oem);
        Assert.Null(kind.Board);
    }

    [Theory]
    [InlineData("Microsoft Corporation", "Virtual Machine")]
    [InlineData("VMware, Inc.", "Intel Corporation")]
    [InlineData("QEMU", "")]
    [InlineData("innotek GmbH", "Oracle Corporation")]
    [InlineData("System manufacturer", "System Product Name")]
    [InlineData("To Be Filled By O.E.M.", "To Be Filled By O.E.M.")]
    public void Placeholders_and_virtual_machines_are_custom_without_oem_or_board(string manufacturer, string board)
    {
        var kind = Classify(manufacturer, board);
        Assert.Null(kind.Oem);
        Assert.Null(kind.Board);
    }
}
