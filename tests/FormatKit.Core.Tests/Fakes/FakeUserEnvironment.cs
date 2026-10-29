using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeUserEnvironment : IUserEnvironment
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Values => _values;

    public string? Get(string name) => _values.GetValueOrDefault(name);

    public void Set(string name, string value) => _values[name] = value;
}
