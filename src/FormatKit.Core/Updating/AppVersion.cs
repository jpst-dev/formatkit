using System.Globalization;
using System.Text.RegularExpressions;

namespace FormatKit.Core.Updating;

/// <summary>A release version: <c>major.minor.patch[-prerelease]</c>, ordered as in semantic versioning.</summary>
public sealed partial class AppVersion : IComparable<AppVersion>
{
    private readonly int[] _numbers;
    private readonly string? _prerelease;

    private AppVersion(int[] numbers, string? prerelease)
    {
        _numbers = numbers;
        _prerelease = prerelease;
    }

    /// <summary>The version without a <c>v</c> prefix or a <c>+commit</c> suffix.</summary>
    public string Text => string.Join('.', _numbers) + (_prerelease is null ? "" : "-" + _prerelease);

    public static bool TryParse(string? text, out AppVersion? version)
    {
        version = null;
        if (text is null)
            return false;

        var match = Pattern().Match(text);
        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (!match.Success || !int.TryParse(match.Groups[i + 1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        }

        version = new AppVersion(numbers, match.Groups[4].Success ? match.Groups[4].Value : null);
        return true;
    }

    public int CompareTo(AppVersion? other)
    {
        if (other is null)
            return 1;

        for (var i = 0; i < 3; i++)
        {
            if (_numbers[i] != other._numbers[i])
                return _numbers[i].CompareTo(other._numbers[i]);
        }

        return (_prerelease, other._prerelease) switch
        {
            (null, null) => 0,
            (null, _) => 1, // A release is newer than its own prereleases.
            (_, null) => -1,
            _ => ComparePrerelease(_prerelease!, other._prerelease!),
        };
    }

    private static int ComparePrerelease(string left, string right)
    {
        var a = left.Split('.');
        var b = right.Split('.');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var result = ComparePart(a[i], b[i]);
            if (result != 0)
                return result;
        }

        return a.Length.CompareTo(b.Length);
    }

    private static int ComparePart(string left, string right)
    {
        var leftNumber = long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var l);
        var rightNumber = long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var r);
        return (leftNumber, rightNumber) switch
        {
            (true, true) => l.CompareTo(r),
            (true, false) => -1, // Numbers sort before names.
            (false, true) => 1,
            _ => string.CompareOrdinal(left, right),
        };
    }

    [GeneratedRegex(@"^v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
