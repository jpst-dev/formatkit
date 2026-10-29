using System.Text.Json;
using FormatKit.Core.Profiles;

namespace FormatKit.Prepare.Tests.Checks.Apps;

internal static class AppsTestHelpers
{
    /// <summary>A loaded profile whose modules are the given name to JSON pairs.</summary>
    public static LoadedProfile Profile(params (string Module, string Json)[] modules) =>
        new(new Profile(1, "test",
                modules.ToDictionary(m => m.Module, m => JsonDocument.Parse(m.Json).RootElement.Clone())),
            @"C:\profiles\p.json", "hash");
}

/// <summary>Runs a test class under the invariant UI culture so the English strings are asserted.</summary>
public abstract class InvariantCultureTests : IDisposable
{
    private readonly System.Globalization.CultureInfo _saved = System.Globalization.CultureInfo.CurrentUICulture;

    protected InvariantCultureTests() =>
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

    public void Dispose() => System.Globalization.CultureInfo.CurrentUICulture = _saved;
}
