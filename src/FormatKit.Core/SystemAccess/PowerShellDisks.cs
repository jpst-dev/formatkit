using System.Globalization;
using System.Text.Json;

namespace FormatKit.Core.SystemAccess;

/// <summary>
/// Lists disks with Get-Disk (joined with partitions, volumes and Win32_DiskDrive) and prepares a USB stick. Removable
/// follows rule R7: BusType USB and a Win32_DiskDrive whose MediaType contains "Removable", or whose Capabilities contain 7
/// (removable media) while MediaType does not contain "hard disk". The prepare script compares the UniqueId itself and
/// exits 4 before any destructive cmdlet runs.
/// </summary>
public sealed class PowerShellDisks(IProcessRunner processes) : IDisks
{
    /// <summary>The largest partition created on a stick (32 GB).</summary>
    public const long MaxPartitionBytes = 32L * 1024 * 1024 * 1024;

    /// <summary>One MiB is left free: a partition as large as the whole disk does not fit after the MBR and alignment.</summary>
    public const long ReservedBytes = 1024 * 1024;

    /// <summary>The size of the partition created on a disk of <paramref name="diskSizeBytes"/>: min(size - 1 MiB, 32 GiB).</summary>
    public static long UsableBytes(long diskSizeBytes) => Math.Min(diskSizeBytes - ReservedBytes, MaxPartitionBytes);

    internal const string ListScript =
        "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'; " +
        "$cim = @{}; try { foreach ($c in @(Get-CimInstance -ClassName Win32_DiskDrive -ErrorAction Stop)) { $cim[[int]$c.Index] = $c } } catch { }; " +
        "$items = @(Get-Disk | ForEach-Object { " +
        "$d = $_; $n = [int]$d.Number; " +
        "$vols = @(); try { $vols = @(Get-Partition -DiskNumber $n -ErrorAction Stop | Get-Volume -ErrorAction SilentlyContinue | ForEach-Object { " +
        "[pscustomobject]@{ Letter = $(if ($_.DriveLetter) { [string]$_.DriveLetter } else { $null }); Label = $_.FileSystemLabel } }) } catch { $vols = @() }; " +
        "$c = $cim[$n]; " +
        "[pscustomobject]@{ Number = $n; UniqueId = [string]$d.UniqueId; FriendlyName = [string]$d.FriendlyName; Size = [int64]$d.Size; " +
        "BusType = [string]$d.BusType; BusTypeValue = [int]$d.BusType; IsBoot = [bool]$d.IsBoot; IsSystem = [bool]$d.IsSystem; " +
        "MediaType = $(if ($c) { [string]$c.MediaType } else { $null }); Capabilities = $(if ($c -and $c.Capabilities) { @($c.Capabilities | ForEach-Object { [int]$_ }) } else { @() }); " +
        "Volumes = @($vols) } }); " +
        "ConvertTo-Json -InputObject $items -Compress -Depth 5";

    public async Task<IReadOnlyList<DiskInfo>> ListAsync(CancellationToken ct)
    {
        var result = await processes.RunAsync(
            new ProcessSpec("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", ListScript], Timeouts.Query),
            ct).ConfigureAwait(false);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException($"Listing disks failed (exit {result.ExitCode}).");
        }

        if (string.IsNullOrWhiteSpace(result.StdOut))
        {
            throw new InvalidOperationException($"Listing disks printed nothing (exit {result.ExitCode}).");
        }

        try
        {
            using var doc = JsonDocument.Parse(result.StdOut);
            var list = new List<DiskInfo>();
            foreach (var item in Items(doc.RootElement))
            {
                list.Add(ParseDisk(item));
            }

            return list;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidOperationException($"Unexpected disk list output (exit {result.ExitCode}).", ex);
        }
    }

    public async Task<string> PrepareUsbAsync(int number, string uniqueId, CancellationToken ct)
    {
        var script = PrepareScript(number, uniqueId);
        var result = await processes.RunAsync(
            new ProcessSpec("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], Timeouts.Media),
            ct).ConfigureAwait(false);

        if (result.ExitCode == 4 && !result.TimedOut)
        {
            throw new DiskChangedException($"Disk {number.ToString(CultureInfo.InvariantCulture)} is no longer the disk that was chosen.");
        }

        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException($"Preparing the USB stick failed (exit {result.ExitCode}).");
        }

        var letter = result.StdOut.Trim();
        if (letter.Length != 1 || !char.IsAsciiLetter(letter[0]))
        {
            throw new InvalidOperationException($"Preparing the USB stick gave no drive letter (exit {result.ExitCode}).");
        }

        return char.ToUpperInvariant(letter[0]) + @":\";
    }

    internal static string PrepareScript(int number, string uniqueId)
    {
        var n = number.ToString(CultureInfo.InvariantCulture);
        var id = PowerShellText.SingleQuote(uniqueId);
        var max = MaxPartitionBytes.ToString(CultureInfo.InvariantCulture);
        return
            "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'; " +
            $"$d = $null; try {{ $d = Get-Disk -Number {n} }} catch {{ exit 4 }}; " +
            $"if ($null -eq $d -or [string]$d.UniqueId -ne {id}) {{ exit 4 }}; " +
            // One MiB is left free: a partition as large as the whole disk does not fit after the MBR and alignment.
            $"$size = [Math]::Min([int64]$d.Size - {ReservedBytes.ToString(CultureInfo.InvariantCulture)}, {max}L); " +
            // Clear-Disk fails on a disk that was never initialized (RAW), for example one an earlier run already cleared.
            $"if ($d.PartitionStyle -ne 'RAW') {{ Clear-Disk -Number {n} -RemoveData -RemoveOEM -Confirm:$false }}; " +
            $"$d = Get-Disk -Number {n}; " +
            $"if ($d.PartitionStyle -eq 'RAW') {{ Initialize-Disk -Number {n} -PartitionStyle MBR }}; " +
            $"$p = New-Partition -DiskNumber {n} -Size $size -IsActive -AssignDriveLetter; " +
            "Format-Volume -Partition $p -FileSystem FAT32 -NewFileSystemLabel 'FORMATKIT' -Confirm:$false | Out-Null; " +
            "($p | Get-Partition).DriveLetter";
    }

    internal static bool IsRemovable(string busType, string? mediaType, IReadOnlyList<int> capabilities)
    {
        if (!string.Equals(busType, "USB", StringComparison.Ordinal))
        {
            return false;
        }

        var media = mediaType ?? string.Empty;
        if (media.Contains("Removable", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return capabilities.Contains(7) && !media.Contains("hard disk", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<JsonElement> Items(JsonElement root) => root.ValueKind switch
    {
        JsonValueKind.Array => root.EnumerateArray().ToList(),
        JsonValueKind.Object => [root],
        JsonValueKind.Null => [],
        _ => throw new JsonException("Unexpected JSON root."),
    };

    private static DiskInfo ParseDisk(JsonElement e)
    {
        var busType = BusTypeName(e);
        var mediaType = e.TryGetProperty("MediaType", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
        var caps = new List<int>();
        if (e.TryGetProperty("Capabilities", out var c))
        {
            foreach (var item in Items(c))
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var v))
                {
                    caps.Add(v);
                }
            }
        }

        var volumes = new List<DiskVolume>();
        if (e.TryGetProperty("Volumes", out var vs))
        {
            foreach (var v in Items(vs))
            {
                var letter = v.TryGetProperty("Letter", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null;
                var label = v.TryGetProperty("Label", out var lb) && lb.ValueKind == JsonValueKind.String ? lb.GetString() : null;
                volumes.Add(new DiskVolume(
                    string.IsNullOrEmpty(letter) ? null : char.ToUpperInvariant(letter[0]),
                    string.IsNullOrEmpty(label) ? null : label));
            }
        }

        return new DiskInfo(
            e.GetProperty("Number").GetInt32(),
            e.GetProperty("UniqueId").GetString() ?? string.Empty,
            e.TryGetProperty("FriendlyName", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() ?? string.Empty : string.Empty,
            e.GetProperty("Size").GetInt64(),
            busType,
            IsRemovable(busType, mediaType, caps),
            Flag(e, "IsBoot"),
            Flag(e, "IsSystem"),
            volumes);
    }

    private static bool Flag(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    // The script prints the enum name; a number (the enum value) is mapped for robustness.
    private static string BusTypeName(JsonElement e)
    {
        var p = e.GetProperty("BusType");
        if (p.ValueKind == JsonValueKind.String)
        {
            return p.GetString() ?? "Unknown";
        }

        return p.GetInt32() switch
        {
            1 => "SCSI",
            2 => "ATAPI",
            3 => "ATA",
            4 => "1394",
            5 => "SSA",
            6 => "Fibre Channel",
            7 => "USB",
            8 => "RAID",
            9 => "iSCSI",
            10 => "SAS",
            11 => "SATA",
            12 => "SD",
            13 => "MMC",
            14 => "Virtual",
            15 => "File Backed Virtual",
            16 => "Storage Spaces",
            17 => "NVMe",
            _ => "Unknown",
        };
    }
}
