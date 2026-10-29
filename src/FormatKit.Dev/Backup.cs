namespace FormatKit.Dev;

internal static class Backup
{
    /// <summary>The name a file is copied to before it is changed: <c>&lt;target&gt;.formatkit-yyyyMMdd-HHmmss.bak</c>.</summary>
    public static string PathFor(string target, DateTimeOffset localNow) =>
        $"{target}.formatkit-{localNow:yyyyMMdd-HHmmss}.bak";
}
