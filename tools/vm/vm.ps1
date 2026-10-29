#Requires -Modules Hyper-V
<#
.SYNOPSIS
    Drives the test VM made by tools\New-TestVm.ps1 over PowerShell Direct.

.DESCRIPTION
    Reads the VM name and credentials from the git-ignored .vm\test-vm.json; nothing secret is
    passed on the command line or printed. Files go to C:\fk in the VM.

      restore  restore the 'clean' checkpoint and wait until the VM answers
      nested   same, with nested virtualization on (needed for WSL 2, scenarios 13-16)
      wait     wait until the VM answers
      copy     copy -File into C:\fk
      run      run -Command in C:\fk as the admin account (PowerShell Direct session, no desktop)
      job      copy -File to C:\fk\job.ps1 and run it on the signed-in user's desktop, elevated,
               through a scheduled task (astask.ps1); with -Visible the window stays open for
               the user to watch and type in (astask-visible.ps1), and this returns at once

    The user must be signed in and connected for 'job' (a basic session in Virtual Machine
    Connection: vmconnect.exe localhost <vm name>).

.EXAMPLE
    .\tools\vm\vm.ps1 restore
    .\tools\vm\vm.ps1 copy -File artifacts\formatkit.exe
    .\tools\vm\vm.ps1 run -Command '.\formatkit.exe --version'
    .\tools\vm\vm.ps1 job -File my-job.ps1 -TimeoutSec 1800
#>
param(
    [Parameter(Mandatory)][ValidateSet('restore', 'nested', 'wait', 'copy', 'run', 'job')][string]$Action,
    [string]$Command,
    [string]$File,
    [int]$TimeoutSec = 1200,
    [switch]$Visible
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$cfg = Get-Content (Join-Path $repo '.vm\test-vm.json') -Raw | ConvertFrom-Json
$vm = $cfg.vmName
$cred = New-Object PSCredential ($cfg.userName, (ConvertTo-SecureString $cfg.password -AsPlainText -Force))

# Right after a boot PowerShell Direct can refuse the credentials or the connection for a while.
function Retry([scriptblock]$Do) {
    for ($i = 1; ; $i++) {
        try { return & $Do }
        catch [System.Management.Automation.Remoting.PSDirectException] { if ($i -ge 8) { throw }; Start-Sleep 10 }
        catch { if ($_.Exception.Message -notmatch 'credencial|credential' -or $i -ge 8) { throw }; Start-Sleep 10 }
    }
}

function Wait-Vm {
    for ($i = 0; $i -lt 120; $i++) {
        try { Invoke-Command -VMName $vm -Credential $cred -ScriptBlock { 1 } -ErrorAction Stop | Out-Null; return }
        catch { Start-Sleep 5 }
    }
    throw 'VM did not answer PowerShell Direct'
}

function Copy-ToVm([string[]]$Paths, [string]$Destination = 'C:\fk\') {
    $s = Retry { New-PSSession -VMName $vm -Credential $cred -ErrorAction Stop }
    try {
        Invoke-Command -Session $s { New-Item -ItemType Directory -Force C:\fk | Out-Null }
        foreach ($p in $Paths) { Copy-Item -ToSession $s -Path $p -Destination $Destination -Force }
    }
    finally { Remove-PSSession $s -ErrorAction SilentlyContinue }
}

function Invoke-InVm([string]$Text) {
    $sb = [scriptblock]::Create("Set-Location C:\fk; `$ProgressPreference='SilentlyContinue'; $Text")
    $s = Retry { New-PSSession -VMName $vm -Credential $cred -ErrorAction Stop }
    try { Invoke-Command -Session $s -ScriptBlock $sb }
    finally { Remove-PSSession $s -ErrorAction SilentlyContinue }
}

switch ($Action) {
    'restore' {
        Restore-VMSnapshot -VMName $vm -Name clean -Confirm:$false
        if ((Get-VM $vm).State -ne 'Running') { Start-VM $vm }
        Wait-Vm; Start-Sleep 20; 'restored'
    }
    'nested' {
        Restore-VMSnapshot -VMName $vm -Name clean -Confirm:$false
        if ((Get-VM $vm).State -ne 'Off') { Stop-VM $vm -TurnOff -Force }
        Set-VMProcessor -VMName $vm -ExposeVirtualizationExtensions $true
        Start-VM $vm
        Wait-Vm; Start-Sleep 20; 'restored with nested virtualization'
    }
    'wait' { Wait-Vm; 'up' }
    'copy' {
        if (-not $File) { throw '-File is required' }
        Copy-ToVm $File; 'copied'
    }
    'run' {
        if (-not $Command) { throw '-Command is required' }
        Invoke-InVm $Command
    }
    'job' {
        if (-not $File) { throw '-File is required' }
        $runner = if ($Visible) { 'astask-visible.ps1' } else { 'astask.ps1' }
        $staged = Join-Path ([IO.Path]::GetTempPath()) 'job.ps1'
        Copy-Item $File $staged -Force
        try { Copy-ToVm @($staged, (Join-Path $PSScriptRoot $runner)) }
        finally { Remove-Item $staged -ErrorAction SilentlyContinue }
        # The VM's execution policy blocks .ps1 files, so the runner is loaded as a script block.
        $user = $cfg.userName -replace "'", "''"
        Invoke-InVm "& ([scriptblock]::Create((Get-Content C:\fk\$runner -Raw))) -TimeoutSec $TimeoutSec -FallbackUser '$user'"
    }
}
