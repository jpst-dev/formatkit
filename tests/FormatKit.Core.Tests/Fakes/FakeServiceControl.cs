using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeServiceControl : IServiceControl
{
    public Dictionary<string, ServiceStartType> Services { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Name, ServiceStartType Type)> Calls { get; } = [];

    public ServiceStartType? GetStartType(string name) =>
        Services.TryGetValue(name, out var type) ? type : null;

    public Task SetStartTypeAsync(string name, ServiceStartType type, CancellationToken ct)
    {
        Calls.Add((name, type));
        Services[name] = type;
        return Task.CompletedTask;
    }
}
