using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public class FakeRegistry : IRegistry
{
    private readonly Dictionary<(string Path, string Name), RegistryValue> _values = new(KeyComparer.Instance);

    public IReadOnlyDictionary<(string Path, string Name), RegistryValue> Values => _values;

    public void Set(string path, string name, RegistryKind kind, string data) =>
        _values[(path, name)] = new RegistryValue(kind, data);

    public virtual RegistryValue? Read(string path, string name) =>
        _values.TryGetValue((path, name), out var value) ? value : null;

    public virtual void Write(string path, string name, RegistryValue value) => _values[(path, name)] = value;

    public void Delete(string path, string name) => _values.Remove((path, name));

    public IReadOnlyList<string> GetSubKeyNames(string path)
    {
        var prefix = path.TrimEnd('\\') + "\\";
        return _values.Keys
            .Where(k => k.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k.Path[prefix.Length..].Split('\\')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed class KeyComparer : IEqualityComparer<(string Path, string Name)>
    {
        public static readonly KeyComparer Instance = new();

        public bool Equals((string Path, string Name) x, (string Path, string Name) y) =>
            string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Path, string Name) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Path),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
    }
}
