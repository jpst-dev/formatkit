namespace FormatKit.Core.SystemAccess;

/// <summary>The current user's persistent environment variables (not the running process's).</summary>
public interface IUserEnvironment
{
    string? Get(string name);

    /// <summary>Stores the value and tells running programs the environment changed.</summary>
    void Set(string name, string value);
}
