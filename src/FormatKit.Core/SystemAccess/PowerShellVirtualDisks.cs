using System.Text.Json;

namespace FormatKit.Core.SystemAccess;

public sealed class PowerShellVirtualDisks(IProcessRunner processes, IFileSystem files, AppPaths paths) : IVirtualDisks
{
    public const string RemountTaskName = "FormatKit-DevDrive";

    // powershell -Command does not populate $args from trailing arguments, so the path is appended as a
    // single-quoted literal after the script block, which does reach $args[0] intact.
    private const string GetScript =
        "& { $img = Get-DiskImage -ImagePath $args[0]; " +
        "$vol = if ($img.Attached) { $img | Get-Disk | Get-Partition | Get-Volume | Where-Object DriveLetter | Select-Object -First 1 }; " +
        "[pscustomobject]@{ Attached = [bool]$img.Attached; " +
        "Letter = if ($vol) { [string]$vol.DriveLetter } else { $null }; " +
        "Label = if ($vol) { [string]$vol.FileSystemLabel } else { $null } } | ConvertTo-Json -Compress }";

    public async Task<VirtualDiskState?> GetAsync(string vhdxPath, CancellationToken ct)
    {
        if (!files.FileExists(vhdxPath))
        {
            return null;
        }

        var output = await RunPowerShellAsync(
            $"{GetScript} {Quote(vhdxPath)}", "Get-DiskImage", ct).ConfigureAwait(false);

        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            var letter = root.GetProperty("Letter").GetString();
            var label = root.GetProperty("Label").GetString();
            return new VirtualDiskState(
                root.GetProperty("Attached").GetBoolean(),
                string.IsNullOrEmpty(letter) ? null : letter[0],
                label);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new VirtualDiskException($"Unexpected Get-DiskImage output: {output.Trim()}", ex);
        }
    }

    public Task AttachAsync(string vhdxPath, CancellationToken ct) =>
        RunPowerShellAsync(MountScript(vhdxPath), "Mount-DiskImage", ct);

    /// <summary>
    /// Attaches the VHDX. Seen in the VM: right after boot Mount-DiskImage fails with "access denied" or "in use"
    /// for a while, so it is retried for about a minute and then diskpart (which still worked) is tried once.
    /// The pipe to diskpart uses the console OEM code page so a non-ASCII path survives.
    /// </summary>
    internal static string MountScript(string vhdxPath) =>
        $"$p = {Quote(vhdxPath)}; " +
        "for ($i = 0; $i -lt 12; $i++) { " +
        "if ((Get-DiskImage -ImagePath $p -ErrorAction SilentlyContinue).Attached) { exit 0 }; " +
        "try { Mount-DiskImage -ImagePath $p -ErrorAction Stop | Out-Null; exit 0 } catch { Start-Sleep -Seconds 5 } }; " +
        "$OutputEncoding = [Console]::OutputEncoding; " +
        "$out = \"select vdisk file=`\"$p`\"\", 'attach vdisk' | diskpart.exe; " +
        "if ($LASTEXITCODE -ne 0) { [Console]::Error.WriteLine(($out -join [Environment]::NewLine)) }; " +
        "exit $LASTEXITCODE";

    public async Task CreateDevDriveAsync(string vhdxPath, int sizeGB, char letter, string label, CancellationToken ct)
    {
        var script = string.Join(Environment.NewLine,
            $"create vdisk file=\"{vhdxPath}\" maximum={sizeGB * 1024L} type=expandable",
            $"select vdisk file=\"{vhdxPath}\"",
            "attach vdisk",
            "convert gpt",
            "create partition primary",
            $"assign letter={letter}");

        // A VHDX that was already there is not ours to clean up when creation fails.
        var existedBefore = files.FileExists(vhdxPath);
        try
        {
            files.CreateDirectory(paths.Root);
            // diskpart's create vdisk fails with "path not found" when the VHDX folder does not exist yet.
            files.CreateDirectory(Path.GetDirectoryName(vhdxPath)!);
            // diskpart reads the script in the console OEM code page, so non-ASCII paths (João) need it, not UTF-8.
            files.WriteAllBytes(paths.DevDriveScriptFile, ProcessOutputDecoder.OemEncoding.GetBytes(script + Environment.NewLine));

            var diskpart = await processes.RunAsync(
                new ProcessSpec("diskpart.exe", ["/s", paths.DevDriveScriptFile], Timeouts.Install), ct).ConfigureAwait(false);
            if (diskpart.ExitCode != 0 || diskpart.TimedOut)
            {
                throw new VirtualDiskException(
                    $"diskpart failed (exit {diskpart.ExitCode}): {(diskpart.StdErr + diskpart.StdOut).Trim()}");
            }

            await RunPowerShellAsync(
                $"Format-Volume -DriveLetter {letter} -DevDrive -NewFileSystemLabel {Quote(label)} -Confirm:$false",
                "Format-Volume", ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var original = ex as VirtualDiskException ?? new VirtualDiskException($"Creating the Dev Drive failed: {ex.Message}", ex);
            if (!existedBefore && files.FileExists(vhdxPath))
            {
                try
                {
                    await DetachAndDeleteAsync(vhdxPath, ct).ConfigureAwait(false);
                }
                catch (Exception cleanup) when (cleanup is not OperationCanceledException)
                {
                    throw new VirtualDiskException(
                        $"{original.Message} (cleanup of '{vhdxPath}' also failed: {cleanup.Message})", original);
                }
            }

            throw original;
        }
        finally
        {
            if (files.FileExists(paths.DevDriveScriptFile))
            {
                files.Delete(paths.DevDriveScriptFile);
            }
        }
    }

    public async Task DetachAndDeleteAsync(string vhdxPath, CancellationToken ct)
    {
        await RunPowerShellAsync(
            $"Dismount-DiskImage -ImagePath {Quote(vhdxPath)} -ErrorAction SilentlyContinue | Out-Null",
            "Dismount-DiskImage", ct).ConfigureAwait(false);

        if (files.FileExists(vhdxPath))
        {
            files.Delete(vhdxPath);
        }
    }

    public bool IsLetterInUse(char letter) =>
        DriveInfo.GetDrives().Any(d => char.ToUpperInvariant(d.Name[0]) == char.ToUpperInvariant(letter));

    public long FreeBytes(string hostRoot) => new DriveInfo(hostRoot).AvailableFreeSpace;

    public Task RegisterRemountTaskAsync(string vhdxPath, CancellationToken ct)
    {
        // The mount script travels base64-encoded (UTF-16LE, as -EncodedCommand expects), so no quoting nests.
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(MountScript(vhdxPath)));
        var argument = $"-NoProfile -NonInteractive -EncodedCommand {encoded}";
        var script =
            $"$a = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument {Quote(argument)}; " +
            "$t = New-ScheduledTaskTrigger -AtStartup; " +
            "$p = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest; " +
            "$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries; " +
            $"Register-ScheduledTask -TaskName '{RemountTaskName}' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null";
        return RunPowerShellAsync(script, "Register-ScheduledTask", ct);
    }

    private static string Quote(string value) => PowerShellText.SingleQuote(value);

    private async Task<string> RunPowerShellAsync(string script, string operation, CancellationToken ct)
    {
        var result = await processes.RunAsync(
            new ProcessSpec("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], Timeouts.Install),
            ct).ConfigureAwait(false);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new VirtualDiskException($"{operation} failed (exit {result.ExitCode}): {result.StdErr.Trim()}");
        }

        return result.StdOut;
    }
}
