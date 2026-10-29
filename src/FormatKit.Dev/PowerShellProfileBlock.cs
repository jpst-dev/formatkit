namespace FormatKit.Dev;

/// <summary>The block FormatKit appends to a PowerShell profile to start Oh My Posh (spec 6.5).</summary>
internal static class PowerShellProfileBlock
{
    public const string Begin = "# >>> FormatKit oh-my-posh >>>";
    public const string End = "# <<< FormatKit oh-my-posh <<<";

    /// <summary>
    /// <paramref name="existing"/> followed by the block; null when the profile already starts Oh My Posh
    /// (ours or the user's), which is then left alone. The result always begins with <paramref name="existing"/> unchanged.
    /// </summary>
    public static string? Append(string existing, string initLine)
    {
        if (existing.Contains("oh-my-posh init", StringComparison.OrdinalIgnoreCase))
            return null;

        var separator = existing.Length == 0 || existing.EndsWith('\n') ? "" : "\r\n";
        var lead = existing.Length == 0 ? "" : "\r\n";
        return existing + separator + lead + Begin + "\r\n" + initLine + "\r\n" + End + "\r\n";
    }
}
