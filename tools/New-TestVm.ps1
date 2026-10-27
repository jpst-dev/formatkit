#Requires -RunAsAdministrator
#Requires -Modules Hyper-V
<#
.SYNOPSIS
    Creates a Hyper-V test VM and installs Windows 11 from an ISO unattended, no clicks needed.

.DESCRIPTION
    Builds a small second ISO holding autounattend.xml, then creates a Generation 2 VM
    (Secure Boot + vTPM) on the Default Switch with the Windows ISO and the answer ISO attached.
    Windows Setup wipes the VM disk, installs the chosen edition, skips OOBE and creates a local
    administrator with auto-logon. The script presses a key for the "Press any key to boot from
    CD or DVD" prompt and waits until PowerShell Direct answers.

    The test account credentials are written to .vm\test-vm.json (git-ignored). Use them with
    PowerShell Direct:  Invoke-Command -VMName FormatKit-Test -Credential $cred { ... }

.EXAMPLE
    .\tools\New-TestVm.ps1 -IsoPath C:\ISO\Win11.iso
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $IsoPath,
    [string] $VmName = 'FormatKit-Test',
    [string] $Edition = 'Windows 11 Pro',
    # Microsoft's public generic installation key for Windows 11 Pro (installs, does not activate).
    [string] $ProductKey = 'VK7JG-NPHTM-C97JM-9MPGT-3V66T',
    [string] $UserName = 'fktest',
    [int] $ProcessorCount = 4,
    [long] $MemoryBytes = 8GB,
    [long] $DiskBytes = 80GB,
    [string] $SwitchName = 'Default Switch',
    [string] $Locale = 'pt-BR',
    [string] $InputLocale = '0416:00010416',
    [string] $TimeZone = 'E. South America Standard Time',
    [int] $TimeoutMinutes = 45
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$vmDir = Join-Path $repoRoot '.vm'
$credFile = Join-Path $vmDir 'test-vm.json'
$answerIso = Join-Path $vmDir 'autounattend.iso'

if (Get-VM -Name $VmName -ErrorAction SilentlyContinue) { throw "VM '$VmName' already exists." }
$vhdPath = Join-Path (Get-VMHost).VirtualHardDiskPath "$VmName.vhdx"
if (Test-Path $vhdPath) { throw "Disk '$vhdPath' already exists." }

function New-IsoFile([string] $SourceDir, [string] $Path, [string] $Label) {
    # IMAPI2 ships with Windows, so no extra tools are needed to build the answer ISO.
    $image = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
    $image.FileSystemsToCreate = 3 # ISO9660 + Joliet
    $image.VolumeName = $Label
    $image.Root.AddTree($SourceDir, $false)
    $stream = $image.CreateResultImage().ImageStream
    if (-not ('FormatKit.IStreamCopy' -as [type])) {
        Add-Type -Namespace FormatKit -Name IStreamCopy -MemberDefinition @'
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
    [FormatKit.IStreamCopy]::Save($stream, $Path)
}

function Send-VmKey([string] $Name, [int] $VirtualKey = 0x0D) {
    $vm = Get-CimInstance -Namespace root\virtualization\v2 -ClassName Msvm_ComputerSystem -Filter "ElementName='$Name'"
    $keyboard = Get-CimAssociatedInstance -InputObject $vm -ResultClassName Msvm_Keyboard
    if ($keyboard) { Invoke-CimMethod -InputObject $keyboard -MethodName TypeKey -Arguments @{ keyCode = [uint32]$VirtualKey } | Out-Null }
}

# Test-only password: random, stored next to the repo, never committed.
New-Item -ItemType Directory -Force $vmDir | Out-Null
$password = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 20 | ForEach-Object { [char]$_ }) + '!a1'
@{ vmName = $VmName; userName = $UserName; password = $password } | ConvertTo-Json | Set-Content -Encoding UTF8 $credFile

$component = 'processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS"'
$unattend = @"
<?xml version="1.0" encoding="utf-8"?>
<unattend xmlns="urn:schemas-microsoft-com:unattend" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
  <settings pass="windowsPE">
    <component name="Microsoft-Windows-International-Core-WinPE" $component>
      <SetupUILanguage><UILanguage>$Locale</UILanguage></SetupUILanguage>
      <InputLocale>$InputLocale</InputLocale>
      <SystemLocale>$Locale</SystemLocale>
      <UILanguage>$Locale</UILanguage>
      <UserLocale>$Locale</UserLocale>
    </component>
    <component name="Microsoft-Windows-Setup" $component>
      <DiskConfiguration>
        <Disk wcm:action="add">
          <DiskID>0</DiskID>
          <WillWipeDisk>true</WillWipeDisk>
          <CreatePartitions>
            <CreatePartition wcm:action="add"><Order>1</Order><Type>EFI</Type><Size>260</Size></CreatePartition>
            <CreatePartition wcm:action="add"><Order>2</Order><Type>MSR</Type><Size>16</Size></CreatePartition>
            <CreatePartition wcm:action="add"><Order>3</Order><Type>Primary</Type><Extend>true</Extend></CreatePartition>
          </CreatePartitions>
          <ModifyPartitions>
            <ModifyPartition wcm:action="add"><Order>1</Order><PartitionID>1</PartitionID><Format>FAT32</Format><Label>System</Label></ModifyPartition>
            <ModifyPartition wcm:action="add"><Order>2</Order><PartitionID>3</PartitionID><Format>NTFS</Format><Label>Windows</Label></ModifyPartition>
          </ModifyPartitions>
        </Disk>
      </DiskConfiguration>
      <ImageInstall>
        <OSImage>
          <InstallFrom><MetaData wcm:action="add"><Key>/IMAGE/NAME</Key><Value>$Edition</Value></MetaData></InstallFrom>
          <InstallTo><DiskID>0</DiskID><PartitionID>3</PartitionID></InstallTo>
        </OSImage>
      </ImageInstall>
      <UserData>
        <AcceptEula>true</AcceptEula>
        <ProductKey><Key>$ProductKey</Key><WillShowUI>OnError</WillShowUI></ProductKey>
      </UserData>
    </component>
  </settings>
  <settings pass="specialize">
    <component name="Microsoft-Windows-Shell-Setup" $component>
      <ComputerName>FK-TEST</ComputerName>
      <TimeZone>$TimeZone</TimeZone>
    </component>
  </settings>
  <settings pass="oobeSystem">
    <component name="Microsoft-Windows-International-Core" $component>
      <InputLocale>$InputLocale</InputLocale>
      <SystemLocale>$Locale</SystemLocale>
      <UILanguage>$Locale</UILanguage>
      <UserLocale>$Locale</UserLocale>
    </component>
    <component name="Microsoft-Windows-Shell-Setup" $component>
      <OOBE>
        <HideEULAPage>true</HideEULAPage>
        <HideOEMRegistrationScreen>true</HideOEMRegistrationScreen>
        <HideOnlineAccountScreens>true</HideOnlineAccountScreens>
        <HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE>
        <ProtectYourPC>3</ProtectYourPC>
      </OOBE>
      <UserAccounts>
        <LocalAccounts>
          <LocalAccount wcm:action="add">
            <Name>$UserName</Name>
            <Group>Administrators</Group>
            <Password><Value>$password</Value><PlainText>true</PlainText></Password>
          </LocalAccount>
        </LocalAccounts>
      </UserAccounts>
      <AutoLogon>
        <Enabled>true</Enabled>
        <Username>$UserName</Username>
        <Password><Value>$password</Value><PlainText>true</PlainText></Password>
        <LogonCount>9999</LogonCount>
      </AutoLogon>
    </component>
  </settings>
</unattend>
"@

$staging = Join-Path $env:TEMP "formatkit-answer-$PID"
New-Item -ItemType Directory -Force $staging | Out-Null
try {
    Set-Content -Path (Join-Path $staging 'autounattend.xml') -Value $unattend -Encoding UTF8
    Remove-Item $answerIso -ErrorAction SilentlyContinue
    New-IsoFile -SourceDir $staging -Path $answerIso -Label 'ANSWERS'
}
finally { Remove-Item $staging -Recurse -Force }

Write-Host "Creating VM $VmName"
New-VM -Name $VmName -Generation 2 -MemoryStartupBytes $MemoryBytes -NewVHDPath $vhdPath -NewVHDSizeBytes $DiskBytes -SwitchName $SwitchName | Out-Null
Set-VMMemory -VMName $VmName -DynamicMemoryEnabled $false
Set-VMProcessor -VMName $VmName -Count $ProcessorCount
Set-VMKeyProtector -VMName $VmName -NewLocalKeyProtector
Enable-VMTPM -VMName $VmName
Set-VM -Name $VmName -CheckpointType Standard -AutomaticCheckpointsEnabled $false
# Guest Service Interface (needed by Copy-VMFile); matched by id because the name is localized.
Get-VMIntegrationService -VMName $VmName | Where-Object { $_.Id -like '*6C09BB55-D683-4DA0-8931-C9BF705F6480' } | Enable-VMIntegrationService
$dvd = Add-VMDvdDrive -VMName $VmName -Path $IsoPath -Passthru
Add-VMDvdDrive -VMName $VmName -Path $answerIso
Set-VMFirmware -VMName $VmName -EnableSecureBoot On -SecureBootTemplate MicrosoftWindows -FirstBootDevice $dvd

Write-Host 'Starting Windows Setup'
Start-VM -Name $VmName
# Answer the "Press any key to boot from CD or DVD" prompt.
for ($i = 0; $i -lt 15; $i++) { Start-Sleep -Seconds 1; Send-VmKey $VmName }

$cred = New-Object PSCredential(".\$UserName", (ConvertTo-SecureString $password -AsPlainText -Force))
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
Write-Host "Waiting for Windows to finish installing (up to $TimeoutMinutes minutes)"
while ($true) {
    try {
        Invoke-Command -VMName $VmName -Credential $cred -ScriptBlock { $env:COMPUTERNAME } -ErrorAction Stop | Out-Null
        break
    }
    catch {
        if ((Get-Date) -gt $deadline) { throw "Windows did not become reachable within $TimeoutMinutes minutes." }
        Start-Sleep -Seconds 20
    }
}

# Setup is done: drop the install media so later reboots go straight to the disk.
Get-VMDvdDrive -VMName $VmName | Remove-VMDvdDrive
Write-Host "VM ready. Credentials: $credFile"
