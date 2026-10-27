namespace FormatKit.Core.SystemAccess;

public sealed record AppxState(bool InstalledForUser, bool Provisioned);

public interface IAppxPackages
{
    /// <summary>Reports whether the package is installed for the user and provisioned for new users. <paramref name="packageName"/> is the AppX <c>Name</c>, matched exactly.</summary>
    Task<AppxState> GetStateAsync(string packageName, CancellationToken ct);

    /// <summary>Removes the user package and the provisioned package.</summary>
    Task RemoveAsync(string packageName, CancellationToken ct);
}
