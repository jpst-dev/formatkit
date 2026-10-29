#Requires -RunAsAdministrator
#Requires -Modules Hyper-V
<#
.SYNOPSIS
    Creates a Hyper-V VM that boots Windows Setup from a folder made by `formatkit media build`.

.DESCRIPTION
    Packs the media folder into .vm\media\media.iso with IMAPI2, with a UEFI boot image
    (efisys_noprompt.bin) so no "Press any key" prompt appears, then creates a Generation 2 VM
    (Secure Boot + vTPM) on the Default Switch with that ISO as the first boot device and an empty disk.

    The script never starts the VM: start it yourself and watch Setup in vmconnect.
    It refuses the VM name FormatKit-Test (the regular test VM). -Replace removes an existing VM
    of the given name and its own disk first; the disk file is deleted only under .vm\media\.

.EXAMPLE
    .\tools\vm\New-MediaVm.ps1 -MediaFolder .vm\media\out -Replace
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $MediaFolder,
    [string] $VmName = 'FormatKit-Media',
    [switch] $Replace,
    [int] $ProcessorCount = 4,
    [long] $MemoryBytes = 4GB,
    [long] $DiskBytes = 64GB,
    [string] $SwitchName = 'Default Switch'
)

$ErrorActionPreference = 'Stop'

# Never touch the regular test VM.
if ($VmName -ieq 'FormatKit-Test') { throw "Refusing VM name '$VmName': it is the regular test VM. Use another name." }
# Get-VM and Remove-VM take wildcards, and the name builds the disk path: allow plain names only.
if ($VmName -notmatch '^[A-Za-z0-9][A-Za-z0-9 ._-]*$') { throw "Invalid VM name '$VmName': use letters, digits, space, '.', '_' and '-' only, starting with a letter or digit." }
if ($VmName.Contains('..')) { throw "Invalid VM name '$VmName': '..' is not allowed." }

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$mediaDir = Join-Path $repoRoot '.vm\media'
$isoPath = Join-Path $mediaDir 'media.iso'
$vhdPath = Join-Path $mediaDir "$VmName.vhdx"

# Validate the media folder before anything is created or removed.
if (-not (Test-Path -LiteralPath $MediaFolder -PathType Container)) {
    # A relative path that does not resolve from the current directory may be relative to the repo root.
    $fromRepo = Join-Path $repoRoot $MediaFolder
    if (-not [System.IO.Path]::IsPathRooted($MediaFolder) -and (Test-Path -LiteralPath $fromRepo -PathType Container)) { $MediaFolder = $fromRepo }
    else { throw "Media folder '$MediaFolder' does not exist." }
}
$MediaFolder = (Resolve-Path -LiteralPath $MediaFolder).ProviderPath
$bootImage = Join-Path $MediaFolder 'efi\microsoft\boot\efisys_noprompt.bin'
if (-not (Test-Path -LiteralPath (Join-Path $MediaFolder 'sources') -PathType Container)) { throw "'$MediaFolder' has no 'sources' folder: it is not a media folder made by 'formatkit media build'." }
foreach ($required in @($bootImage, (Join-Path $MediaFolder 'efiootootx64.efi'), (Join-Path $MediaFolder 'sourcesoot.wim'))) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "'$required' not found: it is not a media folder made by 'formatkit media build'." }
}
# Exact-name lookups (no wildcards), done before anything is removed.
Get-VMSwitch -Name $SwitchName -ErrorAction Stop | Out-Null
$existing = @(Get-VM | Where-Object Name -ieq $VmName)
if ($existing | Where-Object Name -ieq 'FormatKit-Test') { throw 'Refusing to touch the regular test VM FormatKit-Test.' }
if ($existing -and -not $Replace) { throw "VM '$VmName' already exists. Use -Replace to remove it and its disk first." }

function New-IsoFile([string] $SourceDir, [string] $Path, [string] $Label, [string] $BootImagePath) {
    # IMAPI2 ships with Windows, so no extra tools are needed to build the ISO.
    if (-not ('FormatKit.IStreamCopy' -as [type])) {
        Add-Type -Namespace FormatKit -Name IStreamCopy -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, PreserveSig = false)]
static extern void SHCreateStreamOnFileEx(string file, uint mode, uint attributes, bool create, System.IntPtr template, out System.Runtime.InteropServices.ComTypes.IStream stream);
public static object OpenRead(string path) {
    System.Runtime.InteropServices.ComTypes.IStream stream;
    SHCreateStreamOnFileEx(path, 0x40 /* STGM_READ | STGM_SHARE_DENY_NONE */, 0x80 /* FILE_ATTRIBUTE_NORMAL */, false, System.IntPtr.Zero, out stream);
    return stream;
}
public static void Save(object comStream, string path) {
    var stream = (System.Runtime.InteropServices.ComTypes.IStream)comStream;
    var buffer = new byte[65536];
    var read = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
    try {
        using (var file = System.IO.File.Create(path)) {
            while (true) {
                stream.Read(buffer, buffer.Length, read);
                int n = System.Runtime.InteropServices.Marshal.ReadInt32(read);
                if (n == 0) break;
                file.Write(buffer, 0, n);
            }
        }
    } finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(read); }
}
'@
    }
    $image = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
    $image.FileSystemsToCreate = 6 # Joliet + UDF (long names and deep trees)
    $image.VolumeName = $Label
    $image.FreeMediaBlocks = 0 # no size limit (the default is about 650 MB, too small for Windows media)
    $image.Root.AddTree($SourceDir, $false)
    # UEFI El Torito boot image (PlatformId 0xEF), no emulation.
    $boot = New-Object -ComObject IMAPI2FS.BootOptions
    $boot.PlatformId = 0xEF
    $boot.Emulation = 0
    $bootStream = [FormatKit.IStreamCopy]::OpenRead($BootImagePath)
    try {
        $boot.AssignBootImage($bootStream)
        $image.BootImageOptions = $boot
        [FormatKit.IStreamCopy]::Save($image.CreateResultImage().ImageStream, $Path)
    }
    finally { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($bootStream) }
}

New-Item -ItemType Directory -Force $mediaDir | Out-Null

if ($existing) {
    # Remove only the VM with this name and its own disks.
    $disks = @($existing | Get-VMHardDiskDrive | ForEach-Object { $_.Path })
    Write-Host "Removing VM $VmName"
    $existing | Stop-VM -TurnOff -Force -ErrorAction SilentlyContinue
    $existing | Remove-VM -Force
    foreach ($disk in $disks) {
        $full = [System.IO.Path]::GetFullPath($disk)
        if ($full.StartsWith($mediaDir + '\', [System.StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $full -Force }
        else { Write-Warning "Disk '$full' is outside '$mediaDir': left in place." }
    }
}
if (Test-Path -LiteralPath $vhdPath) { throw "Disk '$vhdPath' already exists. Use -Replace (if its VM exists) or delete it." }

try { Remove-Item -LiteralPath $isoPath -ErrorAction Stop -Force }
catch [System.Management.Automation.ItemNotFoundException] { }
catch { throw "Cannot remove '$isoPath' (it may be in use by a running VM): $($_.Exception.Message)" }
Write-Host "Packing $MediaFolder into $isoPath"
New-IsoFile -SourceDir $MediaFolder -Path $isoPath -Label 'FORMATKIT_MEDIA' -BootImagePath $bootImage

Write-Host "Creating VM $VmName"
New-VM -Name $VmName -Generation 2 -MemoryStartupBytes $MemoryBytes -NewVHDPath $vhdPath -NewVHDSizeBytes $DiskBytes -SwitchName $SwitchName | Out-Null
Set-VMMemory -VMName $VmName -DynamicMemoryEnabled $false
Set-VMProcessor -VMName $VmName -Count $ProcessorCount
Set-VMKeyProtector -VMName $VmName -NewLocalKeyProtector
Enable-VMTPM -VMName $VmName
Set-VM -Name $VmName -CheckpointType Standard -AutomaticCheckpointsEnabled $false
$dvd = Add-VMDvdDrive -VMName $VmName -Path $isoPath -Passthru
Set-VMFirmware -VMName $VmName -EnableSecureBoot On -SecureBootTemplate MicrosoftWindows -FirstBootDevice $dvd

Write-Host "VM $VmName is ready and NOT started. Next steps:"
Write-Host "  Start-VM -Name '$VmName'"
Write-Host "  vmconnect.exe localhost '$VmName'"
Write-Host '  Watch Windows Setup in the vmconnect window.'
