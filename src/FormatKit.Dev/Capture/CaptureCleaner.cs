using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FormatKit.Core.Capture;

namespace FormatKit.Dev.Capture;

/// <summary>
/// Drops what the profile parser would reject, so a value this machine happens to hold (a theme in a folder, a swap of 0)
/// costs only that value. The parser stays the one definition of what is valid.
/// </summary>
internal static partial class CaptureCleaner
{
    /// <summary>Each pass can expose a new error: removing a required value reports it as missing.</summary>
    private const int MaxPasses = 8;

    [GeneratedRegex(@"\A(?<name>[^\[]+)(\[(?<index>\d+)\])?\z")]
    private static partial Regex Segment();

    public static void Clean(JsonObject section, DevEnvironment env, List<CaptureNote> notes)
    {
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            using var doc = JsonDocument.Parse(section.ToJsonString());
            var (_, errors) = DevOptions.Parse(doc.RootElement, DevCapture.SectionPath, null, env);
            if (errors.Count == 0)
            {
                Prune(section);
                return;
            }

            // Backwards, so removing one array item leaves the indexes of the earlier errors right.
            foreach (var error in errors.Reverse())
            {
                if (Remove(section, error.Path))
                    notes.Add(new CaptureNote(CaptureNoteKind.NotCaptured, error.Path, error.Message));
            }
        }

        throw new InvalidOperationException("The captured dev section could not be made valid.");
    }

    /// <summary>Removes the node at the path; a path that does not exist (a required value now missing) removes its parent. True when the node existed.</summary>
    private static bool Remove(JsonObject section, string path)
    {
        var relative = path.StartsWith(DevCapture.SectionPath + ".", StringComparison.Ordinal)
            ? path[(DevCapture.SectionPath.Length + 1)..]
            : path;
        var segments = relative.Split('.');

        JsonNode? parent = section;
        JsonObject? grandparent = null;
        string? parentName = null;
        for (var i = 0; i < segments.Length; i++)
        {
            var match = Segment().Match(segments[i]);
            var name = match.Groups["name"].Value;
            if (parent is not JsonObject current)
                return false;

            var isLast = i == segments.Length - 1;
            if (!current.ContainsKey(name))
            {
                if (isLast && grandparent is not null && parentName is not null)
                    grandparent.Remove(parentName);
                return false;
            }

            if (isLast)
            {
                if (match.Groups["index"].Success && current[name] is JsonArray array)
                    array.RemoveAt(int.Parse(match.Groups["index"].Value));
                else
                    current.Remove(name);
                return true;
            }

            grandparent = current;
            parentName = name;
            parent = current[name];
        }

        return false;
    }

    /// <summary>A part left with nothing in it says nothing, so it goes too.</summary>
    private static void Prune(JsonObject node)
    {
        foreach (var (key, child) in node.ToList())
        {
            if (child is not JsonObject inner)
                continue;
            Prune(inner);
            if (inner.Count == 0)
                node.Remove(key);
        }
    }
}
