namespace FormatKit.Core.SystemAccess;

public enum RegistryKind
{
    Dword,
    Qword,
    String,

    /// <summary>REG_EXPAND_SZ. Read-only: <see cref="IRegistry.Read"/> returns the text unexpanded; <see cref="IRegistry.Write"/> rejects it.</summary>
    ExpandString,

    /// <summary>REG_BINARY. <see cref="RegistryValue.Data"/> is lowercase hex without separators.</summary>
    Binary,
}

/// <summary>A registry value. <paramref name="Data"/> is unsigned decimal text for Dword/Qword, the text itself for String, lowercase hex without separators for Binary.</summary>
public sealed record RegistryValue(RegistryKind Kind, string Data);

public interface IRegistry
{
    /// <summary>Returns null when the key or value is absent. An empty <paramref name="name"/> addresses the key's default value (also for <see cref="Write"/> and <see cref="Delete"/>). Throws <see cref="NotSupportedException"/> for a value that is not REG_DWORD, REG_QWORD, REG_SZ, REG_EXPAND_SZ (returned unexpanded) or REG_BINARY.</summary>
    RegistryValue? Read(string path, string name);

    /// <summary>Creates missing keys. Throws <see cref="NotSupportedException"/> for <see cref="RegistryKind.ExpandString"/>.</summary>
    void Write(string path, string name, RegistryValue value);

    /// <summary>Deleting an absent value is a no-op.</summary>
    void Delete(string path, string name);

    /// <summary>Returns an empty list when the key is absent.</summary>
    IReadOnlyList<string> GetSubKeyNames(string path);

    /// <summary>Read-only. Returns an empty list when the key is absent; the unnamed default value is not listed.</summary>
    IReadOnlyList<string> GetValueNames(string path);
}
