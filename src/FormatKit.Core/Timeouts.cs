namespace FormatKit.Core;

public static class Timeouts
{
    public static readonly TimeSpan Install = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan Download = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan Query = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan Media = TimeSpan.FromMinutes(30);
}
