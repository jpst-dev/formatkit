using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public class FakePowerSchemes : IPowerSchemes
{
    public Guid Active { get; set; } = BuiltInPowerSchemes.Balanced;

    public List<Guid> Schemes { get; } = [BuiltInPowerSchemes.Balanced];

    public List<Guid> Deleted { get; } = [];

    public Task<Guid> GetActiveAsync(CancellationToken ct) => Task.FromResult(Active);

    public Task<IReadOnlyList<Guid>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Guid>>(Schemes.ToList());

    public Task<Guid> DuplicateAsync(Guid source, CancellationToken ct)
    {
        var created = Guid.NewGuid();
        Schemes.Add(created);
        return Task.FromResult(created);
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
