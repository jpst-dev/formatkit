namespace FormatKit.Prepare;

public interface IPrepareCheck
{
    string Id { get; }

    PrepareGroup Group { get; }

    Task<IReadOnlyList<PrepareItem>> RunAsync(PrepareContext context, CancellationToken ct);
}
