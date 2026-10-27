namespace FormatKit.Core.SystemAccess;

public enum ServiceStartType
{
    Automatic,
    Manual,
    Disabled,
}

public interface IServiceControl
{
    /// <summary>Returns null when the service does not exist.</summary>
    ServiceStartType? GetStartType(string name);

    Task SetStartTypeAsync(string name, ServiceStartType type, CancellationToken ct);
}
