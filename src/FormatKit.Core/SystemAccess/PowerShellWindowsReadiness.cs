using System.Globalization;
using System.Text.Json;

namespace FormatKit.Core.SystemAccess;

/// <summary>
/// Reads the Windows 11 readiness facts in one PowerShell script that prints JSON. Build comes from the registry
/// (CurrentBuild), RAM from Win32_ComputerSystem, firmware type from $env:firmware_type (UEFI or Legacy, readable
/// without elevation). Secure Boot uses Confirm-SecureBootUEFI, which throws on legacy BIOS or without elevation; then
/// the UEFISecureBootEnabled registry value is used, and null when neither answers. The TPM is read from Win32_Tpm only
/// when the process is elevated; its SpecVersion ("2.0, 0, 1.38") starts with the major version.
/// </summary>
public sealed class PowerShellWindowsReadiness(IProcessRunner processes) : IWindowsReadiness
{
    internal const string Script =
        "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'; " +
        "$build = [int](Get-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion' -Name CurrentBuild).CurrentBuild; " +
        "$ram = $null; try { $ram = [int64](Get-CimInstance -ClassName Win32_ComputerSystem).TotalPhysicalMemory } catch { }; " +
        "$fw = $null; if ($env:firmware_type) { $fw = [string]$env:firmware_type }; " +
        "$sb = $null; try { $sb = $(if (Confirm-SecureBootUEFI) { 1 } else { 0 }) } catch { " +
        "try { $sb = [int](Get-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\SecureBoot\\State' -Name UEFISecureBootEnabled).UEFISecureBootEnabled } catch { $sb = $null } }; " +
        "$tpm = $null; " +
        "$elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator); " +
        "if ($elevated) { try { $t = Get-CimInstance -Namespace 'root/cimv2/Security/MicrosoftTpm' -ClassName Win32_Tpm; " +
        "if ($t) { $tpm = [string]@($t)[0].SpecVersion } } catch { $tpm = $null } }; " +
        "[pscustomobject]@{ Build = $build; Ram = $ram; Firmware = $fw; SecureBoot = $sb; Tpm = $tpm } | ConvertTo-Json -Compress";

    public async Task<ReadinessInfo> ReadAsync(CancellationToken ct)
    {
        var result = await processes.RunAsync(
            new ProcessSpec("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", Script], Timeouts.Query),
            ct).ConfigureAwait(false);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException($"Reading the Windows 11 requirements failed (exit {result.ExitCode}).");
        }

        if (string.IsNullOrWhiteSpace(result.StdOut))
        {
            throw new InvalidOperationException($"Reading the Windows 11 requirements printed nothing (exit {result.ExitCode}).");
        }

        try
        {
            using var doc = JsonDocument.Parse(result.StdOut);
            var e = doc.RootElement;
            return new ReadinessInfo(
                e.GetProperty("Build").GetInt32(),
                Long(e, "Ram"),
                e.TryGetProperty("Firmware", out var f) && f.ValueKind == JsonValueKind.String
                    ? string.Equals(f.GetString(), "UEFI", StringComparison.OrdinalIgnoreCase)
                    : null,
                Flag(e, "SecureBoot"),
                TpmMajor(e));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidOperationException($"Unexpected Windows 11 requirements output (exit {result.ExitCode}).", ex);
        }
    }

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var v) ? v : null;

    private static bool? Flag(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p))
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => p.GetInt32() != 0,
            _ => null,
        };
    }

    private static int? TpmMajor(JsonElement e)
    {
        if (!e.TryGetProperty("Tpm", out var p) || p.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = p.GetString() ?? string.Empty;
        var first = text.Split(',', 2)[0].Trim();
        var major = first.Split('.', 2)[0];
        return int.TryParse(major, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
