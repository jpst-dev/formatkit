# Runs in the VM (see vm.ps1 job). Runs C:\fk\job.ps1 hidden as the signed-in console user,
# elevated, through a scheduled task, waits for it and prints its output.
param([int]$TimeoutSec = 1200, [string]$FallbackUser)
# UserName is empty while the console session is disconnected (a closed enhanced session).
$user = (Get-CimInstance Win32_ComputerSystem).UserName
if (-not $user -and $FallbackUser) { $user = "$env:COMPUTERNAME\$FallbackUser" }
if (-not $user) { throw 'Nobody is signed in to the VM console' }
$name = 'FK-Probe'
Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
Remove-Item C:\fk\job.out, C:\fk\job.done -ErrorAction SilentlyContinue
$a = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument '-NoProfile -ExecutionPolicy Bypass -Command "& { & ([scriptblock]::Create((Get-Content C:\fk\job.ps1 -Raw))) *> C:\fk\job.out; New-Item C:\fk\job.done | Out-Null }"'
$p = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 2)
Register-ScheduledTask -TaskName $name -Action $a -Principal $p -Settings $s | Out-Null
Start-ScheduledTask -TaskName $name
$deadline = (Get-Date).AddSeconds($TimeoutSec)
while (-not (Test-Path C:\fk\job.done) -and (Get-Date) -lt $deadline) { Start-Sleep 5 }
if (-not (Test-Path C:\fk\job.done)) { 'TIMEOUT' }
Get-Content C:\fk\job.out -Raw -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
