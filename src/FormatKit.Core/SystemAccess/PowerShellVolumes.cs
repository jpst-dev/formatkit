using System.Text.Json;

namespace FormatKit.Core.SystemAccess;

/// <summary>
/// Lists fixed volumes with a letter, their disk, BitLocker state and backing VHDX. Read-only. BitLocker is read as the
/// numeric shell property System.Volume.BitLockerProtection, so no display text (language dependent) is parsed.
/// </summary>
public sealed class PowerShellVolumes(IProcessRunner processes) : IVolumes
{
    internal const string Script =
        "$ErrorActionPreference = 'SilentlyContinue'; " +
        "$elevated = $false; try { $elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) } catch { }; " +
        "$shell = $null; try { $shell = New-Object -ComObject Shell.Application } catch { }; " +
        "$items = @(Get-Volume | Where-Object { $_.DriveType -eq 'Fixed' -and $_.DriveLetter } | ForEach-Object { " +
        "$l = [string]$_.DriveLetter; " +
        "$disk = $null; try { $disk = (Get-Partition -DriveLetter $l -ErrorAction Stop | Select-Object -First 1).DiskNumber } catch { $disk = $null }; " +
        "$bl = $null; try { $bl = $shell.NameSpace($l + ':').Self.ExtendedProperty('System.Volume.BitLockerProtection') } catch { $bl = $null }; " +
        "$vhdx = $null; try { $d = Get-Disk -Number $disk -ErrorAction Stop; if ($d.BusType -eq 'File Backed Virtual') { $vhdx = $d.Location } } catch { $vhdx = $null }; " +
        "$rec = $null; if ($elevated) { try { $v = Get-CimInstance -Namespace 'root\\cimv2\\Security\\MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume -Filter (\"DriveLetter='\" + $l + \":'\") -ErrorAction Stop; " +
        "$kp = Invoke-CimMethod -InputObject $v -MethodName GetKeyProtectors -Arguments @{ KeyProtectorType = 3 } -ErrorAction Stop; $rec = (@($kp.VolumeKeyProtectorID).Count -gt 0) } catch { $rec = $null } }; " +
        "[pscustomobject]@{ Letter = $l; Disk = $disk; BitLocker = $bl; Vhdx = $vhdx; Recovery = $rec } }); " +
        "ConvertTo-Json -InputObject $items -Compress";

    public async Task<IReadOnlyList<VolumeInfo>> ListAsync(CancellationToken ct)
    {
        var result = await processes.RunAsync(
            new ProcessSpec("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", Script], Timeouts.Query),
            ct).ConfigureAwait(false);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException($"Listing volumes failed (exit {result.ExitCode}).");
        }

        if (string.IsNullOrWhiteSpace(result.StdOut))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(result.StdOut);
            var root = doc.RootElement;
            var list = new List<VolumeInfo>();
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    list.Add(Parse(item));
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                list.Add(Parse(root));
            }
            else
            {
                throw new JsonException("Unexpected JSON root.");
            }

            return list;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidOperationException($"Unexpected volume list output (exit {result.ExitCode}).", ex);
        }
    }

    internal static BitLockerState MapBitLocker(int? value) => value switch
    {
        1 => BitLockerState.On,
        2 => BitLockerState.Off,
        3 => BitLockerState.Encrypting,
        4 => BitLockerState.Decrypting,
        5 => BitLockerState.Suspended,
        6 => BitLockerState.Locked,
        _ => BitLockerState.Unknown,
    };

    private static VolumeInfo Parse(JsonElement e)
    {
        var letter = e.GetProperty("Letter").GetString();
        if (string.IsNullOrEmpty(letter))
        {
            throw new JsonException("Volume without a letter.");
        }

        var vhdx = e.TryGetProperty("Vhdx", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        bool? recovery = e.TryGetProperty("Recovery", out var r) && r.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? r.GetBoolean()
            : null;
        return new VolumeInfo(char.ToUpperInvariant(letter[0]), Number(e, "Disk"), MapBitLocker(Number(e, "BitLocker")),
            string.IsNullOrEmpty(vhdx) ? null : vhdx, recovery);
    }

    private static int? Number(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n) ? n : null;
}
