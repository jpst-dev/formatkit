using System.Text.Json;

namespace FormatKit.Core.SystemAccess;

/// <summary>
/// Reads the Windows activation state from SoftwareLicensingProduct. The script selects only the status and the
/// channel id; it never selects or prints a product key, full or partial.
/// </summary>
public sealed class CimLicensing(IProcessRunner processes) : ILicensing
{
    internal const string Script =
        "Get-CimInstance SoftwareLicensingProduct -Filter \"ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL\" | " +
        "Select-Object -First 1 LicenseStatus, ProductKeyChannel | ConvertTo-Json -Compress";

    public async Task<WindowsActivation?> GetWindowsActivationAsync(CancellationToken ct)
    {
        var result = await processes.RunAsync(
            new ProcessSpec("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", Script], Timeouts.Query),
            ct).ConfigureAwait(false);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException($"Reading the license status failed (exit {result.ExitCode}).");
        }

        if (string.IsNullOrWhiteSpace(result.StdOut))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(result.StdOut);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                if (root.GetArrayLength() == 0)
                {
                    return null;
                }

                root = root[0];
            }

            var status = root.GetProperty("LicenseStatus").GetInt32();
            var channel = root.TryGetProperty("ProductKeyChannel", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;
            return new WindowsActivation(status == 1, string.IsNullOrEmpty(channel) ? null : channel);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidOperationException($"Unexpected license status output (exit {result.ExitCode}).", ex);
        }
    }
}
