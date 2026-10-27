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

    public virtual Task SetActiveAsync(Guid scheme, CancellationToken ct)
    {
        Active = scheme;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid scheme, CancellationToken ct)
    {
        Schemes.Remove(scheme);
        Deleted.Add(scheme);
        return Task.CompletedTask;
    }
}
