using System.Text.RegularExpressions;

namespace FormatKit.Dev;

/// <summary>Builds the managed <c>~/.zshrc</c> from Oh My Zsh's own template (spec 6.5).</summary>
internal static partial class ZshRc
{
    public const string Marker = "# Managed by FormatKit";

    [GeneratedRegex(@"^ZSH_THEME=.*$", RegexOptions.Multiline)]
    private static partial Regex ThemeLine();

    // The template's list is one line today; a multi-line list is replaced just as well.
    [GeneratedRegex(@"^plugins=\([^)]*\)", RegexOptions.Multiline)]
    private static partial Regex PluginsLine();

    /// <summary>Theme and plugin names are validated by <see cref="DevOptions"/> to letters, digits and . _ -, so they sit safely inside quotes and parentheses.</summary>
    public static string Render(string template, string theme, IReadOnlyList<string> plugins)
    {
        var body = template.Replace("\r\n", "\n");
        body = ThemeLine().Replace(body, _ => $"ZSH_THEME=\"{theme}\"", 1);
        body = PluginsLine().Replace(body, _ => $"plugins=({string.Join(' ', plugins)})", 1);
        return Marker + "\n" + body;
    }

    /// <summary>True when the first line is the marker, i.e. FormatKit wrote the file and may rewrite it.</summary>
    public static bool IsManaged(string existing)
    {
        var text = existing.TrimStart('﻿');
        var end = text.IndexOf('\n');
        var first = end < 0 ? text : text[..end];
        return first.TrimEnd('\r') == Marker;
    }
}
