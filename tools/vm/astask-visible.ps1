# Runs in the VM (see vm.ps1 job -Visible). Opens C:\fk\job.ps1 in a visible window titled
# "FormatKit - test" on the signed-in user's desktop, elevated, with a transcript in C:\fk\job.out.
# Returns at once; C:\fk\job.done appears when the job ends. Inside job.ps1, pipe output to
# Out-Host (or not at all, for steps that prompt) so it shows in the window.
param([int]$TimeoutSec, [string]$FallbackUser)   # TimeoutSec is unused: the caller waits for job.done
$user = (Get-CimInstance Win32_ComputerSystem).UserName
if (-not $user -and $FallbackUser) { $user = "$env:COMPUTERNAME\$FallbackUser" }
if (-not $user) { throw 'Nobody is signed in to the VM console' }
$name = 'FK-Probe'
Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
Remove-Item C:\fk\job.out, C:\fk\job.done -ErrorAction SilentlyContinue
$cmd = '& { $Host.UI.RawUI.WindowTitle = ''FormatKit - test''; Start-Transcript C:\fk\job.out | Out-Null; & ([scriptblock]::Create((Get-Content C:\fk\job.ps1 -Raw))); Stop-Transcript | Out-Null; New-Item C:\fk\job.done | Out-Null }'
$a = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -NoExit -ExecutionPolicy Bypass -Command `"$cmd`""
$p = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 2)
Register-ScheduledTask -TaskName $name -Action $a -Principal $p -Settings $s | Out-Null
Start-ScheduledTask -TaskName $name
Start-Sleep 5
"task state=$((Get-ScheduledTask -TaskName $name).State)"
