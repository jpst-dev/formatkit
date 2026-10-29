using System.Management;
using System.Runtime.InteropServices;
using FormatKit.Hosting.Localization;

namespace FormatKit.Hosting;

/// <summary>The outcome of a guarded system query: the value, or a localized error when the system refused.</summary>
public sealed record QueryResult<T>(T? Value, string? Error);

public static class SystemQuery
{
    /// <summary>Runs a WMI/COM-backed query, turning access and query failures into a localized error.</summary>
    public static async Task<QueryResult<T>> GuardAsync<T>(Func<Task<T>> query)
    {
        try
        {
            return new QueryResult<T>(await query(), null);
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            return new QueryResult<T>(default, Strings.Format("Error_Wmi", ex.Message));
        }
    }
}
