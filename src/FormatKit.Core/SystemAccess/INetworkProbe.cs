namespace FormatKit.Core.SystemAccess;

public interface INetworkProbe
{
    Task<bool> IsOnlineAsync(CancellationToken ct);
}
