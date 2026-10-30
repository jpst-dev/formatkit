using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeScheduledTaskControl : IScheduledTaskControl
{
    /// <summary>Enabled state keyed by <c>path + name</c>; a missing key is an absent task.</summary>
    public Dictionary<string, bool> Tasks { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Path, string Name, bool Enabled)> Changes { get; } = [];

    public Task<bool?> IsEnabledAsync(string path, string name, CancellationToken ct) =>
        Task.FromResult<bool?>(Tasks.TryGetValue(path + name, out var enabled) ? enabled : null);

    public Task SetEnabledAsync(string path, string name, bool enabled, CancellationToken ct)
    {
        Tasks[path + name] = enabled;
        Changes.Add((path, name, enabled));
        return Task.CompletedTask;
    }
}
