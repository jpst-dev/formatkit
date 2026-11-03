namespace FormatKit.Core.SystemAccess;

/// <summary>
/// Mounts ISOs with Mount-DiskImage. The drive letter is read from the volume object, never from display text. An image
/// that is already mounted keeps its mount and its letter is returned.
/// </summary>
public sealed class PowerShellDiskImages(IProcessRunner processes) : IDiskImages
{
    public async Task<string> MountAsync(string isoPath, CancellationToken ct)
    {
        var path = PowerShellText.SingleQuote(isoPath);
        var script =
            "$ErrorActionPreference = 'Stop'; " +
            $"$i = Get-DiskImage -ImagePath {path}; " +
            $"if (-not $i.Attached) {{ $i = Mount-DiskImage -ImagePath {path} -StorageType ISO -PassThru }}; " +
            "($i | Get-Volume).DriveLetter";

        var result = await processes.RunAsync(
            new ProcessSpec("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], Timeouts.Query),
            ct).ConfigureAwait(false);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException($"Mounting the ISO failed (exit {result.ExitCode}).");
        }

        var letter = result.StdOut.Trim();
        if (letter.Length != 1 || !char.IsAsciiLetter(letter[0]))
        {
            throw new InvalidOperationException($"Mounting the ISO gave no drive letter (exit {result.ExitCode}).");
        }

        return char.ToUpperInvariant(letter[0]) + @":\";
    }

    public async Task DismountAsync(string isoPath, CancellationToken ct)
    {
        var script = "$ErrorActionPreference = 'Stop'; " +
            $"Dismount-DiskImage -ImagePath {PowerShellText.SingleQuote(isoPath)} | Out-Null";

        var result = await processes.RunAsync(
            new ProcessSpec("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], Timeouts.Query),
            ct).ConfigureAwait(false);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException($"Dismounting the ISO failed (exit {result.ExitCode}).");
        }
    }
}
