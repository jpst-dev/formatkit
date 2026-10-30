using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public class FakePowerSchemes : IPowerSchemes
{
    public Guid Active { get; set; } = BuiltInPowerSchemes.Balanced;

    public List<Guid> Schemes { get; } = [BuiltInPowerSchemes.Balanced];

    public List<Guid> Deleted { get; } = [];

    /// <summary>The source of every <see cref="DuplicateAsync"/> call, in order.</summary>
    public List<Guid> Duplicated { get; } = [];

    /// <summary>Every <see cref="RenameAsync"/> call, in order.</summary>
    public List<(Guid Scheme, string Name, string Description)> Renames { get; } = [];

    /// <summary>The GUID the next <see cref="DuplicateAsync"/> returns, so a test can seed values for it beforehand.</summary>
    public Guid NextCreated { get; private set; } = Guid.NewGuid();

    /// <summary>Every <see cref="SetValueAsync"/> call, in order.</summary>
    public List<(Guid Scheme, string Subgroup, string Setting, int Ac, int? Dc)> Writes { get; } = [];

    /// <summary>When set, <see cref="SetValueAsync"/> records the call but changes nothing, as if the value did not stick.</summary>
    public bool IgnoreWrites { get; set; }

    private readonly Dictionary<(Guid, string, string), (int Ac, int Dc)> _values = [];

    /// <summary>Seeds a setting's indexes; a setting never seeded is absent.</summary>
    public void SetStored(Guid scheme, string subgroup, string setting, int ac, int dc) =>
        _values[Key(scheme, subgroup, setting)] = (ac, dc);

    private static (Guid, string, string) Key(Guid scheme, string subgroup, string setting) =>
        (scheme, subgroup.ToLowerInvariant(), setting.ToLowerInvariant());

    public Task<(int Ac, int Dc)?> GetValueAsync(Guid scheme, string subgroup, string setting, CancellationToken ct) =>
        Task.FromResult<(int Ac, int Dc)?>(_values.TryGetValue(Key(scheme, subgroup, setting), out var v) ? v : null);

    /// <summary>Throws on a GUID not in <see cref="Schemes"/>, as powercfg does.</summary>
    public Task SetValueAsync(Guid scheme, string subgroup, string setting, int ac, int? dc, CancellationToken ct)
    {
        if (!Schemes.Contains(scheme))
            throw new InvalidOperationException($"powercfg /setacvalueindex failed: no scheme {scheme}");
        Writes.Add((scheme, subgroup, setting, ac, dc));
        if (!IgnoreWrites)
        {
            var key = Key(scheme, subgroup, setting);
            _values[key] = (ac, dc ?? (_values.TryGetValue(key, out var old) ? old.Dc : 0));
        }

        return Task.CompletedTask;
    }

    public Task<Guid> GetActiveAsync(CancellationToken ct) => Task.FromResult(Active);

    public Task<IReadOnlyList<Guid>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Guid>>(Schemes.ToList());

    public Task<Guid> DuplicateAsync(Guid source, CancellationToken ct)
    {
        var created = NextCreated;
        NextCreated = Guid.NewGuid();
        Duplicated.Add(source);
        Schemes.Add(created);
        return Task.FromResult(created);
    }

    public Task RenameAsync(Guid scheme, string name, string description, CancellationToken ct)
    {
        if (!Schemes.Contains(scheme))
            throw new InvalidOperationException($"powercfg /changename failed: no scheme {scheme}");
        Renames.Add((scheme, name, description));
        return Task.CompletedTask;
    }

    /// <summary>Throws on a GUID not in <see cref="Schemes"/>, as powercfg does.</summary>
    public virtual Task SetActiveAsync(Guid scheme, CancellationToken ct)
    {
        if (!Schemes.Contains(scheme))
            throw new InvalidOperationException($"powercfg /setactive failed: no scheme {scheme}");
        Active = scheme;
        return Task.CompletedTask;
    }

    /// <summary>Throws on a GUID not in <see cref="Schemes"/>, as powercfg does.</summary>
    public Task DeleteAsync(Guid scheme, CancellationToken ct)
    {
        if (!Schemes.Remove(scheme))
            throw new InvalidOperationException($"powercfg /delete failed: no scheme {scheme}");
        Deleted.Add(scheme);
        return Task.CompletedTask;
    }
}
