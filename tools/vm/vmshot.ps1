#Requires -Modules Hyper-V
<#
.SYNOPSIS
    Saves a screenshot of the test VM's console to .vm\<Name>.png (git-ignored) and prints the path.
#>
param([string]$Name = 'vm')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$vmName = (Get-Content (Join-Path $repo '.vm\test-vm.json') -Raw | ConvertFrom-Json).vmName
$vm = Get-CimInstance -Namespace root\virtualization\v2 -ClassName Msvm_ComputerSystem -Filter "ElementName='$vmName'"
$svc = Get-CimInstance -Namespace root\virtualization\v2 -ClassName Msvm_VirtualSystemManagementService
$sd = Get-CimAssociatedInstance -InputObject $vm -ResultClassName Msvm_VirtualSystemSettingData | Where-Object VirtualSystemType -eq 'Microsoft:Hyper-V:System:Realized'
$w = 800; $h = 600
$r = Invoke-CimMethod -InputObject $svc -MethodName GetVirtualSystemThumbnailImage -Arguments @{ TargetSystem = $sd; WidthPixels = [uint16]$w; HeightPixels = [uint16]$h }
$data = [byte[]]$r.ImageData
if (-not $data) { return "no image (state: $((Get-VM $vmName).State))" }
# The thumbnail is RGB565, two bytes per pixel.
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap $w, $h
for ($y = 0; $y -lt $h; $y++) {
    for ($x = 0; $x -lt $w; $x++) {
        $i = ($y * $w + $x) * 2
        if ($i + 1 -ge $data.Length) { break }
        $p = [int]$data[$i] -bor ([int]$data[$i + 1] -shl 8)
        $bmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb((($p -shr 11) -band 31) * 8, (($p -shr 5) -band 63) * 4, ($p -band 31) * 8))
    }
}
$out = Join-Path $repo ".vm\$Name.png"
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$out
