using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FormatKit.Media.Bundle;

/// <summary>Sets a string at a JSON path such as <c>modules.programs.direct[2].path</c>; the path must already exist.</summary>
internal static partial class JsonPathWriter
{
    public static void Set(JsonNode root, string jsonPath, string value)
    {
        var steps = new List<(string? Name, int? Index)>();
        foreach (var part in jsonPath.Split('.'))
        {
            var m = SegmentPattern().Match(part);
            if (!m.Success)
                throw new InvalidOperationException($"Invalid JSON path: {jsonPath}");
            steps.Add((m.Groups["name"].Value, null));
            foreach (Capture c in m.Groups["idx"].Captures)
                steps.Add((null, int.Parse(c.Value)));
        }

        JsonNode current = root;
        for (var i = 0; i < steps.Count; i++)
        {
            var (name, index) = steps[i];
            var last = i == steps.Count - 1;
            if (name is not null)
            {
                if (current is not JsonObject obj || !obj.ContainsKey(name))
                    throw new InvalidOperationException($"JSON path not found: {jsonPath}");
                if (last) { obj[name] = JsonValue.Create(value); return; }
                current = obj[name] ?? throw new InvalidOperationException($"JSON path not found: {jsonPath}");
            }
            else
            {
                if (current is not JsonArray arr || index!.Value >= arr.Count)
                    throw new InvalidOperationException($"JSON path not found: {jsonPath}");
                if (last) { arr[index.Value] = JsonValue.Create(value); return; }
                current = arr[index.Value] ?? throw new InvalidOperationException($"JSON path not found: {jsonPath}");
            }
        }
    }

    [GeneratedRegex(@"^(?<name>[^\[\]]+)(\[(?<idx>\d+)\])*$")]
    private static partial Regex SegmentPattern();
}
