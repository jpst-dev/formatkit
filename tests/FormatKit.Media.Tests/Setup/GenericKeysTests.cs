using FormatKit.Media.Setup;

namespace FormatKit.Media.Tests.Setup;

public class GenericKeysTests
{
    [Fact]
    public void ProKey() => Assert.Equal("VK7JG-NPHTM-C97JM-9MPGT-3V66T", GenericKeys.For("Windows 11 Pro"));

    [Fact]
    public void LookupIsCaseInsensitive() => Assert.Equal("VK7JG-NPHTM-C97JM-9MPGT-3V66T", GenericKeys.For("windows 11 pro"));

    [Fact]
    public void UnknownEditionIsNull() => Assert.Null(GenericKeys.For("Windows 11 Ultimate"));

    [Fact]
    public void ListsAllTwelveEditions() => Assert.Equal(12, GenericKeys.Editions.Count);
}
