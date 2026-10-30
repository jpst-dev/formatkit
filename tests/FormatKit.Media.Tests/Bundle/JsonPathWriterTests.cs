using System.Text.Json.Nodes;
using FormatKit.Media.Bundle;

namespace FormatKit.Media.Tests.Bundle;

public class JsonPathWriterTests
{
    [Fact]
    public void Sets_nested_property_and_array_item_property()
    {
        var root = JsonNode.Parse("""{"a":{"b":"x","list":[{"path":"1"},{"path":"2"}]}}""")!;
        JsonPathWriter.Set(root, "a.b", "new");
        JsonPathWriter.Set(root, "a.list[1].path", "other");
        Assert.Equal("new", (string?)root["a"]!["b"]);
        Assert.Equal("1", (string?)root["a"]!["list"]![0]!["path"]);
        Assert.Equal("other", (string?)root["a"]!["list"]![1]!["path"]);
    }

    [Theory]
    [InlineData("a.missing")]
    [InlineData("a.list[5].path")]
    [InlineData("x.y")]
    [InlineData("a.b.c")]
    public void Missing_path_throws(string path)
    {
        var root = JsonNode.Parse("""{"a":{"b":"x","list":[{"path":"1"}]}}""")!;
        Assert.Throws<InvalidOperationException>(() => JsonPathWriter.Set(root, path, "v"));
    }
}
