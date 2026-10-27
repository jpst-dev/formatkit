namespace FormatKit.Core.SystemAccess;

public enum RegistryKind
{
    Dword,
    Qword,
    String,
}

/// <summary>A registry value. <paramref name="Data"/> is unsigned decimal text for Dword/Qword, the text itself for String.</summary>
public sealed record RegistryValue(RegistryKind Kind, string Data);

public interface IRegistry
{
    /// <summary>Returns null when the key or value is absent. Throws <see cref="NotSupportedException"/> for a value that is not REG_DWORD, REG_QWORD or REG_SZ.</summary>
    RegistryValue? Read(string path, string name);

    /// <summary>Creates missing keys.</summary>
    void Write(string path, string name, RegistryValue value);

    /// <summary>Deleting an absent value is a no-op.</summary>
    void Delete(string path, string name);

    /// <summary>Returns an empty list when the key is absent.</summary>
    IReadOnlyList<string> GetSubKeyNames(string path);
}
