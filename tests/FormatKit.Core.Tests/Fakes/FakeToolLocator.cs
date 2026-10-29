using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeToolLocator : IToolLocator
{
    private readonly Dictionary<string, string> _tools = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string exeName, string path) => _tools[exeName] = path;

    public string? Find(string exeName, IReadOnlyList<string> knownPaths) => _tools.GetValueOrDefault(exeName);
}
