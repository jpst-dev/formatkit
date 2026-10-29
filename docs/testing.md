# Manual test script (Hyper-V)

FormatKit changes the system. Run this script in a disposable VM, never first on your main machine. Each item lists the expected result.

## Setup

1. Build or download `formatkit.exe` (`dotnet publish src/FormatKit.Cli -c Release -o artifacts`).
2. In Hyper-V create a **Generation 2** VM with a fresh **Windows 11** (at least 4 GB RAM, a virtual switch, TPM and Secure Boot enabled). Install Windows and finish OOBE with a local account. Shortcut, from an elevated PowerShell on the host:

   ```powershell
   .\tools\New-TestVm.ps1 -IsoPath C:\path\to\Win11.iso
   ```

   It installs Windows unattended (local admin with auto-logon, credentials in the git-ignored `.vm\test-vm.json`) and waits until PowerShell Direct answers. Take the `clean` checkpoint right after.
3. Take a checkpoint named `clean`. Restore it between scenarios.
4. Create a shared folder on the host (for example `C:\FormatKitShare`) reachable from the VM (Enhanced Session drive redirection or an SMB share).

   With a VM made by `New-TestVm.ps1`, `tools\vm\vm.ps1` replaces the shared folder: it restores the checkpoint, copies files into `C:\fk` and runs commands or job scripts in the VM, using the credentials in `.vm\test-vm.json`. Its help (`Get-Help .\tools\vm\vm.ps1`) lists the actions.

**Expected:** the VM boots to the desktop with internet access; the checkpoint exists.

## 1. Fresh VM sanity

In the VM, copy `formatkit.exe` and `profiles\example.json` to `C:\fk`, then:

```powershell
cd C:\fk
.\formatkit.exe hardware
.\formatkit.exe drivers scan
.\formatkit.exe run --profile example.json --dry-run
```

**Expected:** hardware is listed (labels in the Windows UI language); the dry run prints the plan (network check, Windows Update, OEM tool, GPU, leftover report) and exits with code 0 (`$LASTEXITCODE`). Nothing is installed.

## 2. Network driver backup (on the host)

On the physical host (or any machine with working network drivers):

```powershell
.\formatkit.exe drivers backup-network --output C:\FormatKitShare\netdrivers
```

**Expected (confirm):** exit code 0; the output folder contains one subfolder per physical network adapter that uses an OEM INF, each with the exported `.inf`, `.sys` and `.cat` files. Adapters with no INF or on inbox drivers are skipped. Confirm that running the command again also succeeds. If any export fails, the command lists the failed adapters and exits with code 1.

## 3. Run with the network disabled (critical stop, with and without a backup)

A Hyper-V VM uses the inbox synthetic network driver, which cannot bind a physical NIC driver and cannot be removed with `pnputil /delete-driver`. So this scenario only checks what a VM can show: the step's messages, logging and exit code. The real "driver missing, fallback restores the network" check is in the hardware checklist below.

Setup: restore checkpoint `clean`, copy `formatkit.exe`, the profile and the backed-up `netdrivers` folder into the VM (for example `C:\fk\netdrivers`).

**3a. No backup configured**

1. Disable the VM network adapter (Hyper-V settings, or `Disable-NetAdapter` in the VM).
2. Run `.\formatkit.exe run --profile example.json` and accept the UAC prompt.

**Expected:** the network check step fails with the actionable "no backup" message (configure `networkDriverBackup`), steps that need the network are not attempted, and the run stops with exit code **3** (StoppedOnCriticalFailure). The elevated second window (UAC relaunch) waits for **Enter** at the end.

**3b. Backup configured**

1. Keep the adapter disabled. Edit the VM copy of the profile so `modules.drivers` contains `"networkDriverBackup": "C:\\fk\\netdrivers"` (JSON needs doubled backslashes; the path must match the copied folder).
2. Run `.\formatkit.exe run --profile example.json --verbose` and accept the UAC prompt.

**Expected:** the log shows `pnputil /add-driver ... /subdirs /install` running against that folder; the step then fails with the "installed from ... but there is still no internet connection" message; the run exits with code **3**.

Cleanup only: re-enable the adapter afterwards.

Also decline the UAC prompt on one run. **Expected:** exit code **5** (ElevationDeclined).

## 4. Reboot required: countdown, cancel, scheduled task, resume

1. Restore `clean` and enable the network. A fresh VM usually has nothing that needs a restart, so force one with the test switch, set machine-wide so the resume task sees it after the reboot (elevated PowerShell):
   `[Environment]::SetEnvironmentVariable('FORMATKIT_TEST_REBOOT_AFTER', 'drivers.windows-update', 'Machine')`
   The named step runs normally and then reports `RebootRequired`; any step id from `--dry-run` works.
2. Run `.\formatkit.exe run --profile example.json` from an elevated console, so the countdown has the keyboard focus (with UAC the countdown runs in the elevated child's window).
3. When the reboot countdown appears, press **Esc**.
4. Check the task: `schtasks /query /fo LIST /v | findstr /i formatkit` (or Task Scheduler).
5. Run again until the countdown appears and let it finish.
6. After the VM reboots, log on and watch.

**Expected:**
- The countdown is displayed; Esc cancels it and the VM does not reboot; the resume scheduled task exists (trigger: at logon) and the run is left to resume at the next logon (exit code **4**, RebootPending).
- When the countdown completes the VM reboots; after logon the scheduled task starts `formatkit run --resume` in its own console window (the task passes the hidden `--wait-on-exit` flag, so the window waits for Enter at the end unless the exit code is 4) and the run continues from the next step instead of restarting.
- `.\formatkit.exe run --resume` with no interrupted run exits with code **6** (NothingToResume).
- When done, clear the switch: `[Environment]::SetEnvironmentVariable('FORMATKIT_TEST_REBOOT_AFTER', $null, 'Machine')`.

## 5. Report contents

Reports (`report.md` and `report.json`) are written only when a run finishes (completed or failed). A run that exits with code 4 (RebootPending) writes no report; the report appears after the post-reboot `--resume` run completes. Open the report of the last finished run (its folder is printed at the end of the run).

**Expected:** it contains the overall status, each step's title and outcome, the step messages, and a "next steps" list for steps that need manual action. No credentials or personal data.

## 6. Task removed at the end

After a run completes successfully (all steps done, no pending reboot):

```powershell
schtasks /query /fo LIST | findstr /i formatkit
```

**Expected:** no FormatKit scheduled task remains, and `run --resume` exits with code 6.

## 7. Cleanup apply and undo

The cleanup levels are described in the README; the tweaks FormatKit refuses to apply are in [cleanup-not-included.md](cleanup-not-included.md). This scenario applies the **whole** catalog and checks that undo returns the machine to where it started.

1. Restore `clean`. Copy `formatkit.exe` to `C:\fk`.
2. In `C:\fk` create `cleanup-aggressive.json`:

   ```json
   {
     "schemaVersion": 1,
     "name": "Cleanup aggressive",
     "modules": {
       "cleanup": { "enabled": true, "level": "aggressive", "confirmAggressive": true }
     }
   }
   ```

   Run `.\formatkit.exe run --profile cleanup-aggressive.json --dry-run` first. **Expected:** the plan lists the cleanup items with their level and contested mark; nothing changes. Items needing a battery check (power plans) are skipped on laptops only; the VM has no battery, so they are included.
3. Export the registry before any change (elevated PowerShell):

   ```powershell
   reg export HKLM\SOFTWARE before-sw.reg /y
   reg export HKLM\SYSTEM\CurrentControlSet before-sys.reg /y
   reg export HKCU before-cu.reg /y
   ```

4. Run `.\formatkit.exe run --profile cleanup-aggressive.json` and accept the UAC prompt. Memory Integrity needs a reboot, so the countdown appears; let it finish, log on and let the resume task finish the run.

   **Expected:** the run ends with exit code **0** (or 4 for the run that reboots, then 0 after resume); the preinstalled apps in the catalog are gone, and the resume run reports the remaining items.
5. Run `.\formatkit.exe cleanup check`.

   **Expected:** exit code **1**. Memory Integrity (and VBS) are listed under "Security risks:" (the "reduces security" group), the contested values (for example `SystemResponsiveness`, `Win32PrioritySeparation`) are listed under "Contested tweaks:", and the items just applied appear under "Applied cleanup items".
6. Run `.\formatkit.exe cleanup undo --all` and accept the UAC prompt. The VM needs internet and `winget` installed, because undo reinstalls the removed apps from the Microsoft Store (see scenario 8).

   **Expected:** one "reverted" line per item, exit code **0**.
7. Reboot the VM, then export again:

   ```powershell
   reg export HKLM\SOFTWARE after-sw.reg /y
   reg export HKLM\SYSTEM\CurrentControlSet after-sys.reg /y
   reg export HKCU after-cu.reg /y
   fc.exe before-sw.reg after-sw.reg
   fc.exe before-sys.reg after-sys.reg
   fc.exe before-cu.reg after-cu.reg
   ```

   Reboot from inside Windows (`Restart-Computer`), never with a hard reset such as `Restart-VM -Force`: a reset can drop registry writes that were not flushed yet and makes undo look broken.

   **Expected:** every registry value the catalog touches (the `registry` actions in `cleanup.json`) has the same value, or is absent, exactly as in the `before` export. A whole-hive `fc` is not useful here: the Store reinstalls in step 6 bring newer app versions, which rewrite thousands of keys under `AppModel`, `Appx`, `PackagedCom`, `Local Settings` and `Extensions\ContractId`, and Windows itself changes `FeatureManagement`, per-session service instances (`*_xxxxx`), `bam`, `Schedule` and Search state on every boot. Compare only the catalog targets: parse both exports into `key|value` pairs and check each `path|name` from `cleanup.json`.

   Seen on the first run (Windows 11 Pro build 26300, 2026-10-28): all 22 catalog registry targets matched. The only leftover is an empty `HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity` key: undo removes the `Enabled` value it added but keeps the key it created, which Windows treats as "not configured".

   Any catalog target that differs means undo did not restore a value: file it as a bug. Apps removed by `appx` actions come back through undo (see scenario 8), so also check `Get-AppxPackage` lists the same packages as at `clean`, apart from frameworks the reinstalls pull in (`Microsoft.UI.Xaml`, `VCLibs`, `NET.Native`, `WindowsAppRuntime`, and `Microsoft.StartExperiencesApp` on build 26300) and Solitaire, which winget cannot reinstall (undo fails it with the Store link, so `undo --all` exits 1). Power plans that FormatKit created must be gone from `powercfg /list`.

## 8. App removal and reinstall

1. Restore `clean`, copy `formatkit.exe` to `C:\fk`, and create `cleanup-safe.json` as in scenario 7 with `"level": "safe"` and no `confirmAggressive`.
2. Confirm Clipchamp is present: `Get-AppxPackage Clipchamp.Clipchamp`. Check the signed-in user, not `-AllUsers`: an unattended install leaves a `defaultuser0` profile that keeps its own copy of every inbox app, and FormatKit (like Settings > Apps) removes the app for the current user and from the provisioned image only.
3. Run `.\formatkit.exe run --profile cleanup-safe.json` and accept the UAC prompt.

   **Expected:** exit code 0; `Get-AppxPackage Clipchamp.Clipchamp` returns nothing, and neither does `Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq Clipchamp.Clipchamp`.
4. Run `.\formatkit.exe cleanup undo apps.clipchamp` and accept the UAC prompt (the VM needs internet; `winget` must be installed).

   **Expected:** an `apps.clipchamp: reverted` line, exit code 0, and `Get-AppxPackage Clipchamp.Clipchamp` lists the package again (winget installs the current Store version, which may be newer than the inbox one). With `winget` missing, the item fails with the "install App Installer from the Store" next action instead.

## 9. Diagnosis of a tweaked PC

Restore `clean`. Before changing anything, record the default start types for check (b) below: run `sc.exe qc BITS` and `sc.exe qc AppXSvc` and write down each `START_TYPE`.

In an elevated PowerShell in the VM, mimic what a "gaming tweaks" script does:

```powershell
$hpet = Get-PnpDevice | Where-Object HardwareID -contains 'ACPI\PNP0103'
$vdrv = Get-PnpDevice | Where-Object HardwareID -contains 'ROOT\VDRVROOT'
Disable-PnpDevice -InstanceId $hpet.InstanceId -Confirm:$false
Disable-PnpDevice -InstanceId $vdrv.InstanceId -Confirm:$false
sc.exe config w32time start= disabled
```

Run `.\formatkit.exe cleanup check`.

A Hyper-V Generation 2 VM has no HPET device, so `$hpet` is empty and its `Disable-PnpDevice` line fails; HPET can only be checked on hardware (see the checklist below).

**Expected:** exit code **1**. HPET (hardware only), the Virtual Drive Enumerator (`ROOT\VDRVROOT`) and the Windows Time service are all listed under "Breaks Windows features:", each with a fix command. Run the three fix commands as printed; run `cleanup check` again. **Expected:** none of the three is listed and the exit code is 0 (the VM has no other findings).

Also check, because earlier reviews could not verify these on a real machine:

- **(a) `reg add` service fix.** Disable the Task Scheduler service with the registry (Windows refuses `sc.exe config` for it): `reg add "HKLM\SYSTEM\CurrentControlSet\Services\Schedule" /v Start /t REG_DWORD /d 4 /f`. Run `cleanup check` and confirm the Task Scheduler (`Schedule`) is listed under "Breaks Windows features:" with a `reg add ... /d 2 /f` fix. Run that fix command exactly as printed, then confirm `sc.exe qc Schedule` shows `START_TYPE : 2 AUTO_START`, `cleanup check` no longer lists it, and after a reboot `Get-Service Schedule` is `Running`. Repeat with Base Filtering Engine (`BFE`) if time allows. **Expected:** the service is restored in both cases. Restore `clean` afterwards if anything stays broken.
- **(b) Default start types.** Use the `START_TYPE` values recorded at the start of this scenario, on the fresh `clean` checkpoint. The fixes in `known-issues.json` set BITS to Manual (`sc.exe config BITS start= demand`) and AppXSvc to `Start` = 2 (`reg add ... /d 2 /f`). If the recorded default differs, correct that fix, and the "Manual" or "Automatic" wording in [cleanup-not-included.md](cleanup-not-included.md). Result (Windows 11 Pro build 26300, 2026-10-28): BITS `Start` = 3 (Manual), AppXSvc `Start` = 2 (Automatic), both matching the fixes; UsoSvc is 2 with `DelayedAutostart` = 1, matching `start= delayed-auto`.
- **(c) Windows Home.** If a Windows 11 **Home** VM is available, apply a `level: balanced` profile (as scenario 8, with `"level": "balanced"`) and check whether the policy-based privacy items (`privacy.advertising-id`, `privacy.tailored-experiences`, `privacy.diagnostic-data`) take effect there: after applying, open Settings > Privacy & security > General and Diagnostics & feedback and confirm the settings show the new values (or are greyed out as managed) and stay after a reboot. Otherwise note this check as **pending** (no Home VM yet).

## 10. Programs: mixed profile, run twice

The VM needs internet and `winget` (App Installer). The signed-in user is the local account from OOBE.

1. Restore `clean`. Copy `formatkit.exe` to `C:\fk`.
2. Download a small `.exe` installer you trust (for example the 7-Zip installer from its official site) to `C:\fk\setups` and compute its hash: `(Get-FileHash C:\fk\setups\<file>.exe -Algorithm SHA256).Hash`. Use it as `sha256` below, with the silent switch of that installer in `args`.
3. In `C:\fk` create `programs-mixed.json`:

   ```json
   {
     "schemaVersion": 1,
     "name": "Programs mixed",
     "modules": {
       "programs": {
         "enabled": true,
         "acceptAgreements": true,
         "catalog": ["browsers.firefox", "communication.slack", "productivity.notion"],
         "winget": [
           { "id": "Notepad++.Notepad++" },
           { "id": "9NKSQGP7F2NH", "source": "msstore" },
           { "id": "Spotify.Spotify", "context": "user" }
         ],
         "direct": [
           { "name": "Local installer", "path": "C:\\fk\\setups\\<file>.exe", "sha256": "<hash from step 2>", "args": ["/S"] }
         ]
       }
     }
   }
   ```

   Run `.\formatkit.exe run --profile programs-mixed.json --dry-run`. **Expected:** the plan lists every program with its key and name (a `[user]` marker for the per-user ones); nothing is installed.
4. Run `.\formatkit.exe run --profile programs-mixed.json` and accept the UAC prompt.

   **Expected:** exit code **0** (or 1 if the Store is unreachable; the report then names the failed item and the others still install). Firefox, Notepad++ and the direct installer's program appear in Settings > Apps. A reboot, if an installer asks for one, goes through the normal countdown and resume (scenario 4).
5. In a **non-elevated** PowerShell as the signed-in user, run `winget list --id Spotify.Spotify`.

   **Expected:** Spotify is listed. It was installed with `context: user` (a temporary scheduled task as the signed-in user), so it must not exist only under the administrator account. Also run `winget list --id SlackTechnologies.Slack` and `winget list --id Notion.Notion`: the catalog installs both per user (their machine-wide installer is an MSIX, which winget cannot install device-wide), so both must be listed for the signed-in user.
6. Run the same command again.

   **Expected:** exit code 0 and nothing is downloaded or run a second time. Steps whose programs are all installed (and the direct step, from `%ProgramData%\FormatKit\programs-direct.json`) are skipped with "Nothing to do: already in place.". `Spotify.Spotify` is a free `winget` entry, so it is in the `programs.other` step (with Notepad++ and the Store app), not in a category step. The `programs.other` step may still run: the elevated check of a per-user package only sees it when the elevated user is the signed-in user (here they are the same account, so it is usually skipped too). If it runs, every item in it reports "already installed" and nothing is installed again.
7. Sign-in failure. Restore `clean`. While still signed in, let winget set up its package source once (for example `.\formatkit.exe programs search notepad`): with nobody signed in, winget cannot set up its source on an account that never used it, and every winget item then fails with "no network or source unavailable" (winget code 0x8A15000F). Then sign out of the VM console (Start > sign out, or `logoff <session id>`, where the session id is the `SessionId` of `explorer.exe`), so nobody is signed in. From the host, run FormatKit through PowerShell Direct, using the VM credentials kept outside the repo (as in the other scenarios; do not write them in a file of the repo), with `C:\fk\formatkit.exe` and a `programs-signout.json` already copied into the VM beforehand, holding `browsers.firefox` in `catalog` and one `{ "id": "Spotify.Spotify", "context": "user" }` in `winget`:

   ```powershell
   $cred = Get-Credential   # the VM admin account
   Invoke-Command -VMName <vm-name> -Credential $cred -ScriptBlock {
       & C:\fk\formatkit.exe run --profile C:\fk\programs-signout.json
   }
   ```

   **Expected:** the Spotify item fails with "Sign in to Windows and run again." as its next action, Firefox installs normally, and the exit code is **1**. Sign in again and run once more: Spotify installs and the run ends with 0.

## 11. Programs: wrong hash on a direct installer

1. Restore `clean`. Copy `formatkit.exe` to `C:\fk`.
2. Create `programs-badhash.json` with one direct item whose `sha256` is wrong (64 zeros), pointing at a real https installer:

   ```json
   {
     "schemaVersion": 1,
     "name": "Programs bad hash",
     "modules": {
       "programs": {
         "enabled": true,
         "acceptAgreements": true,
         "direct": [
           { "name": "Bad hash", "url": "https://example.com/setup.exe", "sha256": "0000000000000000000000000000000000000000000000000000000000000000", "args": ["/S"] }
         ]
       }
     }
   }
   ```

   Replace the url with a real, small installer download you trust.
3. Run `.\formatkit.exe run --profile programs-badhash.json` and accept the UAC prompt.

   **Expected:** the item fails with a hash mismatch message, exit code **1**. `Get-ChildItem C:\ProgramData\FormatKit\downloads` shows no installer file (a wrong hash deletes the downloaded file; a local `path` file would never be deleted). Nothing ran: `Get-Process` shows no process of that installer (check while the run is going on and afterwards), and the program is not in Settings > Apps or in `Get-Package`. `%ProgramData%\FormatKit\programs-direct.json` has no entry for the item.
4. Change `url` to `http://...`. **Expected:** the profile is rejected before anything runs, exit code **2**, with the JSON path of the error. Remove the `sha256` from a `url` item: same result.

## 12. Programs: export on one VM, import on another

1. On a prepared VM (restore `clean`, then install a few programs with winget, for example `winget install --id Notepad++.Notepad++` and `winget install --id 7zip.7zip`), copy `formatkit.exe` to `C:\fk` and run `.\formatkit.exe programs export C:\fk\old-pc-programs.json`.

   **Expected:** exit code 0, the file exists and holds the installed packages, and the output explains how to use it as `importFile`. No elevation prompt.
2. Copy the file to the shared folder. Restore `clean` and copy `formatkit.exe` and the file to `C:\fk`.
3. Create `programs-import.json`:

   ```json
   {
     "schemaVersion": 1,
     "name": "Programs import",
     "modules": {
       "programs": {
         "enabled": true,
         "acceptAgreements": true,
         "importFile": "C:\\fk\\old-pc-programs.json"
       }
     }
   }
   ```

   Run `.\formatkit.exe run --profile programs-import.json --dry-run`. **Expected:** the plan lists the exported packages. Then run it for real and accept the UAC prompt.

   **Expected:** the packages are installed (`winget list`); packages winget cannot find again are reported as failed items without stopping the others. A second run skips the fully installed steps with "Nothing to do: already in place." (items winget cannot reinstall fail again). A missing file, or one that is not a `winget export` JSON, gives exit code 2.
4. Run `.\formatkit.exe programs search notepad`. **Expected:** winget's own result list is printed as is, exit code 0; a term nothing matches exits with 1.

## Dev environment scenarios (13-16)

These four scenarios need a Hyper-V VM with **nested virtualization** enabled, because WSL 2 runs its own virtual machine. On the host, with the VM off, run (elevated PowerShell):

```powershell
Set-VMProcessor -VMName <vm name> -ExposeVirtualizationExtensions $true
```

Give the VM at least 8 GB of RAM and 2 or more virtual processors, restore the `clean` checkpoint before each scenario, and keep the shared folder from Setup. The VM needs internet access. Windows build 22621.2338 or newer is required for the Dev Drive (scenario 16). Report lines are described in scenario 5; each dev item is one line, in the form `<item>: done | already in order | pending | not applicable | failed`.

Three things cannot be checked by a program and are verified **by hand** in these scenarios: typing the `passwd` password live (scenario 13), the Windows Terminal fragment font being visible (scenario 15) and `diskpart` with a non-ASCII VHDX path (scenario 16).

Keep the VM's user **signed in and connected** while a scenario runs (in Virtual Machine Connection, a basic session shows the existing desktop; an enhanced session that is closed leaves the user disconnected). Per-user installs such as Oh My Posh fail with "no user signed in" otherwise, and the passwords and passphrases are typed in the console window that FormatKit opens on that desktop.

Last run in a VM (2026-10-29, Windows 11 build 26300, Windows Terminal 1.24): scenarios 13-16 passed after these fixes, each found by these scenarios: a step that needs to run again after the WSL reboot (the engine had marked it complete), the VHDX folder created before `diskpart`, the Dev Drive mount retried at boot with a `diskpart` fallback, `.ssh` created before `ssh-keygen`, a pending Windows PowerShell profile item when scripts are blocked, and the Nerd Font installed only once (reinstalling it while Windows Terminal started crashed the terminal).

## 13. Dev: WSL from nothing, reboot and resume, Linux password

1. Restore `clean`. Copy `formatkit.exe` to `C:\fk`. In `C:\fk` create `dev-wsl.json` (replace `joao` with a lowercase user name of your choice):

   ```json
   {
     "schemaVersion": 1,
     "name": "Dev WSL",
     "modules": {
       "dev": {
         "enabled": true,
         "wsl": {
           "distro": "Ubuntu",
           "user": "joao",
           "wslconfig": { "memory": "4GB", "processors": 2, "swap": "1GB" }
         }
       }
     }
   }
   ```

2. Run `.\formatkit.exe run --profile dev-wsl.json --dry-run`. **Expected:** exit code 0, the plan lists `dev.wsl` marked as needing administrator rights, nothing changes.
3. Run `.\formatkit.exe run --profile dev-wsl.json` and accept the UAC prompt.

   **Expected:** the step installs the WSL platform (`wsl --install --no-distribution`) and reports `WSL: done`. A reboot is **always** required after the platform install: the countdown appears and the run exits with code **4**.
4. Let the VM reboot and log on. **Expected:** the resume task opens a console with `formatkit run --resume`. `dev.wsl` runs **again** (it asked to, unlike a step that is complete after its reboot): it now installs Ubuntu (`wsl --install Ubuntu --no-launch`) and creates the Linux user. If the distro install fails with a network error right after the boot, the next action says to check the internet connection; run the profile again.
5. **By hand:** when `passwd` asks for the new password, type it **live in that console** (twice). Nothing you type is echoed, read or logged by FormatKit.

   **Expected:** the run ends with `WSL: already in order`, `Ubuntu: done`, `user joao: done` and `.wslconfig: done`, and exit code 0 (or 1 only if an item failed; read its next action).
6. Check from a regular PowerShell:

   ```powershell
   wsl -d Ubuntu --exec /usr/bin/id -un
   wsl -d Ubuntu --exec /usr/bin/getent passwd joao
   Get-Content $env:USERPROFILE\.wslconfig
   ```

   **Expected:** `joao` (the default user), a `getent` line for `joao` with `/bin/bash`, and a `[wsl2]` section with `memory=4GB`, `processors=2` and `swap=1GB`. Inside the distro `passwd -S joao` reports `P` (password set), not `L`.
7. Run `.\formatkit.exe run --profile dev-wsl.json` again. **Expected:** every item is `already in order`, the `.wslconfig` is not rewritten (no `.bak` file next to it) and the exit code is 0.
8. Unattended variant: restore `clean`, repeat steps 3 and 4 but do not touch the console after the reboot, with the resumed run's input redirected (or run `.\formatkit.exe run --profile dev-wsl.json < NUL` from an elevated console). **Expected:** the user is created with a locked password, `user joao: pending`, the next action names `formatkit dev wsl-user --profile <your-profile.json>`, and the run exits with code **1**. Then run `.\formatkit.exe dev wsl-user --profile dev-wsl.json` from a normal console and type the password; **Expected:** `user joao: done`, exit code 0. Running the same command with input redirected (`< NUL`) exits with code **2** and a message that it needs a console where you can type.
9. `.\formatkit.exe dev wsl-user` without `--profile` uses `Ubuntu` and your lowercase Windows user name; with a Windows name that is not a valid Linux user name (for example one with a space) it exits with code **2** and asks for `--profile`.

## 14. Dev: Git and SSH key

This scenario does not need scenario 13. Git must be installed first, which the profile below does through the programs module.

1. Restore `clean`. Copy `formatkit.exe` to `C:\fk`. In `C:\fk` create `dev-git-ssh.json`:

   ```json
   {
     "schemaVersion": 1,
     "name": "Dev Git and SSH",
     "modules": {
       "programs": { "enabled": true, "acceptAgreements": true, "catalog": ["development.git"] },
       "dev": {
         "enabled": true,
         "git": {
           "name": "Test User",
           "email": "test@example.com",
           "defaultBranch": "main",
           "editor": "code --wait",
           "autocrlf": "input"
         },
         "ssh": { "keyType": "ed25519", "comment": "test@fk-vm" }
       }
     }
   }
   ```

2. Run `.\formatkit.exe run --profile dev-git-ssh.json` and accept the UAC prompt. When `ssh-keygen` asks for a passphrase, **type one live** in the console (twice); the same for `ssh-add` when it asks.

   **Expected:** `Dev: Git` reports every key as done; `Dev: SSH key` reports `key: done`, `ssh-agent: done` or `already in order`, `agent key: done` and `Git uses Windows SSH: done`. The public key and `https://github.com/settings/ssh/new` are printed on the console and appear in the report details. Exit code 0.
3. Check:

   ```powershell
   git config --global --list
   Get-Service ssh-agent | Select-Object Status, StartType
   ssh-add -l
   Get-Content $env:USERPROFILE\.ssh\id_ed25519.pub
   ```

   **Expected:** `user.name`, `user.email`, `init.defaultBranch=main`, `core.editor=code --wait`, `core.autocrlf=input` and `core.sshCommand` pointing at `C:/Windows/System32/OpenSSH/ssh.exe`; `ssh-agent` Running and Automatic; one key listed (`test@fk-vm`); the `.pub` file ends in `test@fk-vm`.
4. Copy `$env:USERPROFILE\.ssh\id_ed25519` to the shared folder, run `.\formatkit.exe run --profile dev-git-ssh.json` again. **Expected:** `key: already in order`, no passphrase prompt, the key file is byte-identical, every item is `already in order`, exit code 0.
   On a clean Windows `%USERPROFILE%\.ssh` does not exist; FormatKit creates it before `ssh-keygen`.
5. Pending path: restore `clean`, remove the programs line (so the Git items fail: Git is not installed; that is expected in this variant), run only the dev part with input redirected (`.\formatkit.exe run --profile dev-git-ssh.json < NUL` from an elevated console). **Expected:** `key: pending`, the next action names `formatkit dev ssh-key --profile <your-profile.json>`, exit code **1**. Then run `.\formatkit.exe dev ssh-key --profile dev-git-ssh.json` from a non-elevated console, accept the UAC prompt, and type the passphrase. **Expected:** the key is created, exit code 0. `.\formatkit.exe dev ssh-key < NUL` exits with code **2**.

## 15. Dev: dotfiles, zsh, Oh My Zsh and Oh My Posh

Needs scenario 13 done on the same VM (WSL with the user) plus Git and Oh My Posh; restore the state of scenario 13 step 5 or run its profile first.

1. Create a **public** GitHub repository with, at least, `powershell/profile.ps1` (a line like `Write-Host "dotfiles profile"`) and `git/.gitconfig.local` (`[user]` with a name). Keep both files LF. (Without a repository, put the two files in a folder such as `C:\fk\dotfiles` and use `"path": "C:\\fk\\dotfiles"` instead of `repo` and `cloneTo`; `repository` is then reported as not applicable.)
2. In `C:\fk` create `dev-shell.json` (use your repository address and the WSL user from scenario 13):

   ```json
   {
     "schemaVersion": 1,
     "name": "Dev shell",
     "modules": {
       "cleanup": { "enabled": true, "include": ["developer.ps-execution-policy"] },
       "programs": { "enabled": true, "acceptAgreements": true, "catalog": ["development.git", "development.oh-my-posh"] },
       "dev": {
         "enabled": true,
         "wsl": { "distro": "Ubuntu", "user": "joao" },
         "dotfiles": {
           "repo": "https://github.com/<you>/<repo>.git",
           "cloneTo": "%USERPROFILE%\\dotfiles",
           "files": [
             { "from": "powershell/profile.ps1", "to": "%USERPROFILE%\\Documents\\PowerShell\\dotfiles.ps1" },
             { "from": "git/.gitconfig.local", "to": "%USERPROFILE%\\.gitconfig.local" }
           ]
         },
         "shell": {
           "wsl": { "zsh": true, "ohMyZsh": true, "theme": "robbyrussell",
                    "plugins": ["git", "zsh-autosuggestions", "zsh-syntax-highlighting"] },
           "windows": { "ohMyPosh": true, "theme": "paradox", "font": "Meslo" }
         }
       }
     }
   }
   ```

3. Before running, create a **differing** target: `Set-Content $env:USERPROFILE\.gitconfig.local "[user]", "name = Old"`.
4. Run `.\formatkit.exe run --profile dev-shell.json` and accept the UAC prompt.

   **Expected:** `repository: done` (cloned to `%USERPROFILE%\dotfiles`); the differing `.gitconfig.local` is renamed to `.gitconfig.local.formatkit-<yyyyMMdd-HHmmss>.bak` and replaced by the repository's file; `%USERPROFILE%\Documents\PowerShell\dotfiles.ps1` is copied. In `Dev: terminal`: `zsh`, `Oh My Zsh`, `.zshrc` and `login shell` are done; `font Meslo` is done; the PowerShell profile gets the `# >>> FormatKit oh-my-posh >>>` block. Exit code 0.
5. Check:

   ```powershell
   Get-ChildItem $env:USERPROFILE -Filter ".gitconfig.local*"
   Get-Content $env:LOCALAPPDATA\Microsoft\Windows` Terminal\Fragments\FormatKit\formatkit.json
   wsl -d Ubuntu --exec /usr/bin/getent passwd joao
   wsl -d Ubuntu --exec /usr/bin/head -n 1 /home/joao/.zshrc
   ```

   **Expected:** the file and its `.bak`; a JSON fragment that updates the Windows PowerShell and PowerShell 7 profiles with `font.face` `MesloLGM Nerd Font`; the passwd line ends in `/usr/bin/zsh` (or `/bin/zsh`); the first `.zshrc` line is `# Managed by FormatKit`, and the file lists the three plugins and `ZSH_THEME="robbyrussell"`.
6. **By hand:** open a **new** Windows Terminal window (close all existing ones first), start a Windows PowerShell tab and a PowerShell 7 tab. **Expected:** the Oh My Posh `paradox` prompt appears, and the prompt glyphs render correctly, which means the fragment font (`MesloLGM Nerd Font`) is applied. Look at Settings > Profiles > Windows PowerShell > Appearance to confirm the font face. If the glyphs are boxes, the fragment did not take effect: note it in the issue tracker. Then start the Ubuntu tab: `zsh` with the Oh My Zsh prompt, autosuggestions and syntax highlighting.
7. Run the profile again. **Expected:** every item is `already in order`, no new `.bak` file, the PowerShell profile has exactly one FormatKit block, `.zshrc` is unchanged, exit code 0.
8. Blocked scripts check: on a restored state without the `cleanup` line (Windows PowerShell's policy is `Restricted` on a clean Windows), run the profile. **Expected:** the Windows PowerShell profile item is `pending - Restricted`, its next action says to add `developer.ps-execution-policy` to the cleanup include list, and the exit code is 1. With the line back, the item is `already in order` and the exit code is 0.
9. Keep-user-file check: in the distro replace `.zshrc` with your own (`wsl -d Ubuntu --exec /bin/sh -c "echo '# mine' > /home/joao/.zshrc"`) and run again. **Expected:** `.zshrc` is reported with a warning and kept as is.

## 16. Dev: Dev Drive survives a reboot, caches, developer cleanup

1. Restore `clean` and check the build: `[Environment]::OSVersion.Version` must be 10.0.22621.2338 or newer. Copy `formatkit.exe` to `C:\fk`. In `C:\fk` create `dev-drive.json`:

   ```json
   {
     "schemaVersion": 1,
     "name": "Dev Drive",
     "modules": {
       "cleanup": {
         "enabled": true,
         "level": "safe",
         "include": ["developer.developer-mode", "developer.long-paths", "developer.show-extensions",
                     "developer.show-hidden", "developer.sudo", "developer.ps-execution-policy"]
       },
       "dev": {
         "enabled": true,
         "devDrive": {
           "path": "C:\\DevDrive\\dev.vhdx",
           "sizeGB": 50,
           "letter": "D",
           "label": "Dev",
           "caches": ["npm", "nuget", "pip", "cargo"]
         }
       }
     }
   }
   ```

   The disk that holds the VHDX needs at least 60 GB free (the size plus 10 GB); with less, `Dev Drive` fails before creating anything and its detail gives both sizes. If the VM's system disk is smaller, attach a second virtual disk to the VM (for example 100 GB, dynamic), format it as `F:` and use `"path": "F:\\DevDrive\\dev.vhdx"`.
2. Run `.\formatkit.exe run --profile dev-drive.json --dry-run`. **Expected:** the plan lists the cleanup items of the `developer` category and `dev.devdrive`; nothing changes.
3. Run it for real and accept the UAC prompt. **Expected:** the developer cleanup asks for a reboot (the countdown appears; the run resumes after logon), then `Dev Drive: done`, `remount at startup: done`, each cache done, exit code 0. The VHDX folder (`C:\DevDrive`) does not need to exist beforehand.
4. Check:

   ```powershell
   Get-Volume -DriveLetter D | Select-Object FileSystem, FileSystemLabel
   fsutil devdrv query D:
   Get-ScheduledTask -TaskName FormatKit-DevDrive | Select-Object TaskName, State
   Get-ChildItem D:\packages
   [Environment]::GetEnvironmentVariable('npm_config_cache', 'User')
   [Environment]::GetEnvironmentVariable('NUGET_PACKAGES', 'User')
   [Environment]::GetEnvironmentVariable('PIP_CACHE_DIR', 'User')
   [Environment]::GetEnvironmentVariable('CARGO_HOME', 'User')
   ```

   **Expected:** ReFS volume labelled `Dev`; `fsutil` reports the volume as a trusted Dev Drive; the task exists; `D:\packages` holds `npm`, `nuget`, `pip` and `cargo`; the four user variables point inside `D:\packages`.
5. **By hand (non-ASCII path):** repeat on a restored `clean` with `"path": "C:\\Dev Ünicode\\dev.vhdx"` (and `letter` `E`). **Expected:** the drive is created and `fsutil devdrv query E:` works. If diskpart cannot handle the path, the item fails with its next action, the half-created VHDX is detached and deleted so a retry starts clean, and nothing is left under `C:\Dev Ünicode`. Record which of the two happened.
6. Reboot the VM twice and log on each time. **Expected:** `D:` is back with the same letter (the startup task retries `Mount-DiskImage` for about a minute, which fails with "access denied" or "in use" right after boot, then falls back to `diskpart`) and `fsutil devdrv query D:` still reports it as trusted (the `FormatKit-DevDrive` startup task mounted the VHDX). Run the profile again: **Expected:** `Dev Drive: already in order`, caches already in order, exit code 0.
7. Developer cleanup: check Settings > System > For developers (Developer Mode on), `Get-ItemProperty HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem LongPathsEnabled` (1), Explorer shows file extensions and hidden files, `sudo config` reports `inline`, and `Get-ExecutionPolicy -Scope LocalMachine` is `RemoteSigned`.
8. Undo: `.\formatkit.exe cleanup undo --all` and accept the UAC prompt. **Expected:** the six values return to what they were before the run (Developer Mode off, `LongPathsEnabled` 0, extensions and hidden files hidden again, `sudo` as before, execution policy as before). The Dev Drive is **not** touched by undo (the `dev` module has no undo): remove it by hand if needed with `Dismount-DiskImage -ImagePath C:\DevDrive\dev.vhdx`, `Unregister-ScheduledTask FormatKit-DevDrive` and by deleting the file.
9. Cache already set: on a restored `clean`, set `NUGET_PACKAGES` to another folder first (`[Environment]::SetEnvironmentVariable('NUGET_PACKAGES', 'C:\nuget', 'User')`) and run the profile. **Expected:** the other three caches are set, `NUGET_PACKAGES` keeps its value and the run reports a warning for it.

## Preparation checklist scenarios (17-22)

`formatkit prepare check` only reads, but these scenarios build the situations it looks for (repositories, BitLocker, a WSL distro, files on `C:`), so they run in the VM like the others. Restore the `clean` checkpoint before each one (`nested` for scenario 21) and copy `formatkit.exe` into `C:\fk`:

```powershell
.\tools\vm\vm.ps1 restore
.\tools\vm\vm.ps1 copy -File artifacts\formatkit.exe
```

`vm.ps1 run` shows the exit code only if you ask for it, so end each command with `; "exit=$LASTEXITCODE"`. The VM blocks `.ps1` files (execution policy): a script copied into it runs as `& ([scriptblock]::Create((Get-Content C:\fk\x.ps1 -Raw)))`. Scenario 18 needs Git (through winget) and internet access. Scenarios 19, 20 and 22 use the extra data disk `F:`, which restoring `clean` detaches: attach the data VHDX to the VM again (`Add-VMHardDiskDrive` on the host) and check that it is online (`Get-Partition | Select DiskNumber, DriveLetter`). These scenarios call it `F:`; it can come back with another letter (in the 2026-10-29 run it was `D:`), so use the letter it got. The data VHDX is not part of the checkpoint: what a scenario does to it (BitLocker, files) stays for the next one.

To check whether a PowerShell session is elevated, use `([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)`; `IsInRole('Administrator')` (a group name that does not exist) always prints `False`.

Last run in a VM (2026-10-29, Windows 11 build 26100, Brazilian Portuguese UI, virtual TPM): scenarios 17-22 passed after these fixes, each found by these scenarios: the hidden `desktop.ini` in every user folder no longer counts as the user's files, device encryption "waiting for activation" is read as encrypted instead of not checked, and a failed `--output` (or volume listing) no longer repeats its message as an English log warning.

Record what the scenarios find in the "Found while running" section of the [preparation checklist spec](superpowers/specs/2026-10-29-preparation-checklist-design.md).

## 17. Prepare: clean VM, no profile

1. Restore `clean`, copy `formatkit.exe`.
2. Run:

   ```powershell
   .\tools\vm\vm.ps1 run -Command '.\formatkit.exe prepare check; "exit=$LASTEXITCODE"'
   ```

**Expected:** exit code **0** and the last line `No blockers. Review the N items marked Attention before formatting.` (N is the number of Attention items listed). There is no Blocker. Group headings end with `:` and the items are indented below them. Under Attention: `The list of installed programs has not been exported` (`formatkit programs export E:\programs.json`) and `The network drivers have not been backed up` (`formatkit drivers backup-network --output E:\netdrivers`); possibly also the account `defaultuser0` left by the unattended install (`data.other-users`), a Windows activation item (the VM is usually not activated) and a browser item if Edge was ever started. Under Ok: usually the user folders (empty or not on the Windows drive; a folder on `C:` that holds files is an Attention item; the hidden `desktop.ini` Windows puts in each one does not count), and `C: (Windows) is encrypted with BitLocker` when device encryption is on (the VM's `C:` usually is, even "waiting for activation"). No Git, WSL or key item appears. Nothing in the output holds a product key.

## 18. Prepare: Git repositories with unpushed work

1. Restore `clean`, copy `formatkit.exe`, and install Git. winget is per user, so do not use `vm.ps1 run` (no desktop): create `git-install.ps1` on the host with `winget install --id Git.Git -e --accept-source-agreements --accept-package-agreements *> C:\fk\git-install.txt` and run `.\tools\vm\vm.ps1 job -File git-install.ps1` (the user must be signed in and connected), then check `C:\fk\git-install.txt` and `Test-Path 'C:\Program Files\Git\cmd\git.exe'`. Alternatively install it through a small profile with `development.git` in `programs` and `formatkit run`, as scenario 14 does.
2. Copy this script to `C:\fk\repos.ps1` and run it (`vm.ps1 run -Command '& ([scriptblock]::Create((Get-Content C:\fk\repos.ps1 -Raw)))'`). It uses the `Documents` folder of the account that runs it; `vm.ps1 run` and the check in step 3 both run as the VM admin account, so they see the same folder. The bare repository stands in for GitHub:

   ```powershell
   $git = 'C:\Program Files\Git\cmd\git.exe'
   $docs = "$env:USERPROFILE\Documents"
   & $git init --bare C:\remote.git
   foreach ($name in 'app-a', 'app-b') {
       & $git init -b $name "$docs\$name"
       Set-Content "$docs\$name\readme.txt" $name
       & $git -C "$docs\$name" add .
       & $git -C "$docs\$name" -c user.name=Test -c user.email=t@example.com commit -m first
       & $git -C "$docs\$name" remote add origin C:/remote.git
       & $git -C "$docs\$name" push -u origin $name
   }
   # app-a: a commit that is not pushed
   Add-Content "$docs\app-a\readme.txt" more
   & $git -C "$docs\app-a" -c user.name=Test -c user.email=t@example.com commit -am second
   # app-b: an uncommitted change
   Add-Content "$docs\app-b\readme.txt" more
   ```

3. Run `.\tools\vm\vm.ps1 run -Command '.\formatkit.exe prepare check --verbose; "exit=$LASTEXITCODE"'`.

   **Expected:** exit code **1**, last line `2 items block formatting.`. Under Blocker (group Developer), one item per repository: `Repository ...\app-a has work that exists only on this PC` with `Branch app-a has 1 commit not pushed.`, and `Repository ...\app-b ...` with `1 uncommitted change.`, both with the action "Commit and push, or copy the folder ... to an external drive.". The text `remote.git` appears nowhere in the output or the log (`--verbose` prints the log): FormatKit never prints a remote URL.
4. Commit and push both: in `app-b` run `git -c user.name=Test -c user.email=t@example.com commit -am third`, then `git push` in both repositories. Run the check again.

   **Expected:** exit code **0**, `2 repositories, all pushed` under Ok (its detail says that files ignored by Git, such as `.env`, are not in the remote), and the last line starts with `No blockers.` (the program list and network driver items are still Attention).
5. A repository with no remote at all is a Blocker too: `git init` a third folder in `Documents` with one commit and no `origin`. **Expected:** exit code **1** and the item says `The repository has no remote.`.
6. Nested repositories are inspected too: `git init` the folder `Documents\app-a\vendor\lib`, commit one file there with no `origin`, and run the check. **Expected:** a separate Blocker for `...\app-a\vendor\lib` with `The repository has no remote.` (app-a also reports the new folder as an uncommitted change). The same holds for a repository at the root of the user profile (`git init $env:USERPROFILE`): the repositories below it are still listed.

## 19. Prepare: profile inputs on C: and off C:

1. Restore `clean`, copy `formatkit.exe`, attach the data disk so `F:` exists.
2. In the VM create the two inputs on `C:`: `.\formatkit.exe programs export C:\fk\programs.json` and `New-Item C:\fk\netdrivers -ItemType Directory; Set-Content C:\fk\netdrivers\net.inf '; test'` (the check only looks for a `.inf` file).
3. Create `C:\fk\prepare.json`:

   ```json
   {
     "schemaVersion": 1,
     "name": "Prepare inputs",
     "modules": {
       "drivers": { "enabled": true, "sources": ["windowsUpdate"], "networkDriverBackup": "C:\\fk\\netdrivers" },
       "programs": { "enabled": true, "acceptAgreements": true, "importFile": "C:\\fk\\programs.json" }
     }
   }
   ```

4. Run `.\tools\vm\vm.ps1 run -Command '.\formatkit.exe prepare check --profile C:\fk\prepare.json; "exit=$LASTEXITCODE"'`.

   **Expected:** exit code **1**. Under Blocker (group Apps and FormatKit): `The program list C:\fk\programs.json is on C: and will be erased` and `The network driver backup C:\fk\netdrivers is on C: and will be erased`, the first two with the action to move it off `C:` and update the profile (the program list and driver backup actions name `modules.programs.importFile` and `modules.drivers.networkDriverBackup`), and `The profile file is on C: and will be erased` with the action `Move it off C: and update the profile.` (the profile itself lives on `C:`). No path is reported twice.
5. Move everything to `F:`: `Copy-Item C:\fk\programs.json, C:\fk\prepare.json F:\; Copy-Item C:\fk\netdrivers F:\ -Recurse`, then edit `F:\prepare.json` so the two paths are `F:\\netdrivers` and `F:\\programs.json`. Run `.\formatkit.exe prepare check --profile F:\prepare.json`.

   **Expected:** exit code **0**. `The program list F:\programs.json is off C:` and `The network driver backup F:\netdrivers is off C:` under Ok, plus `1 profile file is off C:` (the profile).
6. Also check: a `networkDriverBackup` folder on `F:` without any `.inf` file is a Blocker (`... is missing or has no drivers`, action `Run formatkit drivers backup-network --output ...`); an invalid profile (for example `"schemaVersion": 9`) prints its errors and exits with **2** without running any check.

## 20. Prepare: BitLocker on the data disk

1. On the host check whether the VM has a virtual TPM: `Get-VMSecurity -VMName <vm name>` and read `TpmEnabled`. **If it is `False`, the scenario is not covered:** do not enable BitLocker, and record "scenario 20: not covered (no vTPM)" in the "Found while running" section of the spec. If it is `True`, continue.
2. Restore `clean`, copy `formatkit.exe`, attach the data disk (`F:`). In the VM, in an elevated PowerShell (`vm.ps1 run` is assumed to be elevated; step 4 confirms it with the `IsInRole` line), turn BitLocker on for `F:` and wait until `Get-BitLockerVolume F:` shows `ProtectionStatus` `On`:

   ```powershell
   Enable-BitLocker -MountPoint F: -RecoveryPasswordProtector -UsedSpaceOnly -SkipHardwareTest
   ```

   If Windows device encryption already encrypted the disk ("waiting for activation", no protectors), adding the protector leaves `ProtectionStatus` `Off`; run `Resume-BitLocker -MountPoint F:` to turn it on.

3. **Not elevated.** In the VM console open a normal (not "Run as administrator") PowerShell and run `C:\fk\formatkit.exe prepare check`. To be sure it is not elevated: `([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)` must print `False`. (Without going to the console: a `vm.ps1 job` script can register a scheduled task for the signed-in user with `New-ScheduledTaskPrincipal -LogonType Interactive -RunLevel Limited` that runs the check with its output redirected to a file.)

   **Expected:** under Attention `F: is encrypted with BitLocker`, with the action to save the recovery key (`aka.ms/myrecoverykey` or `manage-bde -protectors -get F:`); under Not checked `Recovery key of F: not checked`, with the action `manage-bde -protectors -get F:`. No UAC prompt appears. The recovery password never appears in the output.
4. **Elevated.** Run `.\tools\vm\vm.ps1 run -Command '.\formatkit.exe prepare check; "exit=$LASTEXITCODE"'` (confirm with the `IsInRole` line that the session prints `True`).

   **Expected:** `F: is encrypted with BitLocker` stays under Attention and the Not checked item for F: is gone, because the recovery password protector was read. Remove the protector (`manage-bde -protectors -delete F: -Type RecoveryPassword`) and run again: the Attention action now says `F: has no recovery password` and gives the `manage-bde -protectors -add F: -RecoveryPassword` command. Exit code **0** in every case (no Blocker).

## 21. Prepare: WSL distribution

1. Restore the **`nested`** checkpoint (`.\tools\vm\vm.ps1 nested`), copy `formatkit.exe`, and install one distro by running scenario 13 up to step 5. Keep the user signed in and connected.
2. WSL is registered per user, so run the check on that user's desktop: create `prepare-wsl.ps1` on the host (in the repo working directory; `job -File` takes a host path and copies it into the VM) with `C:\fk\formatkit.exe prepare check *> C:\fk\prepare.txt; "exit=$LASTEXITCODE" | Add-Content C:\fk\prepare.txt` and run `.\tools\vm\vm.ps1 job -File prepare-wsl.ps1`, then read `C:\fk\prepare.txt`. (Running the command in a console of the signed-in user works too. On the 2026-10-29 run `vm.ps1 run`, which uses the same account, also listed the distro; the job is the safe default.) To make the distro without scenario 13's profile and its live `passwd`, run `wsl --install --no-distribution` through `job`, restart the VM, then `wsl --install Ubuntu --no-launch` through `job`.

   **Expected:** under Attention (group Developer) `The WSL distribution Ubuntu will be erased` with the action `Export it with wsl --export Ubuntu E:\Ubuntu.tar ...`. Docker's own distros are never listed. Exit code **0**. On the plain `clean` checkpoint (no WSL) there is no WSL item.

## 22. Prepare: `--output` on and off the Windows drive

1. Restore `clean`, copy `formatkit.exe`, attach the data disk so `F:` exists. To see a Blocker box too, you can first do scenario 18 steps 1 and 2.
2. Run:

   ```powershell
   .\tools\vm\vm.ps1 run -Command '.\formatkit.exe prepare check --output F:\checklist.md; "exit=$LASTEXITCODE"; Get-Content F:\checklist.md'
   ```

   **Expected:** the report is printed, then `Checklist saved to F:\checklist.md`, with no warning. The file starts with the title `FormatKit preparation checklist` and the computer name, has the date, a heading per status, `- [ ]` before every Blocker and Attention item and a plain `-` bullet for the Not checked and Ok items. Exit code **0** (or **1** if a Blocker exists). Running it again overwrites the file.
3. Run `.\tools\vm\vm.ps1 run -Command '.\formatkit.exe prepare check --output C:\fk\c.md; "exit=$LASTEXITCODE"'`.

   **Expected:** the console prints `Warning: C:\fk\c.md is on a drive the format will erase, so the checklist will be lost. Copy it elsewhere.` and the file is written anyway. Then run it with `--output Z:\nope\c.md` (a drive that does not exist): the report is printed first, then `Could not write ...`, and the exit code is **2**.
4. Switch the VM display language to Brazilian Portuguese (skip if it already is, as the test VM made from a Portuguese ISO) and repeat step 2. **Expected:** the console and the Markdown file are in Portuguese.

## Installation media scenarios (23-27)

These scenarios check `formatkit media build` and `formatkit media usb` (spec: [installation media](superpowers/specs/2026-10-29-installation-media-design.md), section 11). Scenarios 23-26 use a separate disposable VM, `FormatKit-Media`, so the regular test VM and its `clean` checkpoint stay untouched. **Scenario 27 uses your own spare USB stick on the host: you run it, nobody else. Claude never runs `media usb`.**

You need the Windows 11 x64 ISO and, for scenario 24, the profile inputs. Build the executable and the media from an elevated PowerShell on the host (`media build` mounts the ISO read-only and writes only under `.vm\media\`):

```powershell
dotnet publish src/FormatKit.Cli -c Release -o artifacts
artifacts\formatkit.exe media build --iso C:\path\to\Win11.iso --output .vm\media\out
.\tools\vm\New-MediaVm.ps1 -MediaFolder .vm\media\out -Replace
```

`New-MediaVm.ps1` packs the folder into `.vm\media\media.iso` (UEFI boot image without the "Press any key" prompt), creates the Generation 2 VM `FormatKit-Media` (Secure Boot, vTPM, 4 GB RAM, 64 GB disk, Default Switch) with that ISO first in the boot order, and **does not start it**. It refuses the name `FormatKit-Test`. Start the VM yourself (`vmconnect localhost FormatKit-Media`, then Start) and watch Setup. For each new scenario build the media again into an empty folder (delete `.vm\media\out` first, or use another `--output`) and run the script with `-Replace`.

Record what the scenarios find in the "Found while running" section of the installation media spec; spec section 6 lists three details to confirm (the local account screen on current Windows 11, `$OEM$` copying, the first logon token).

## 23. Media: no profile

1. Build as above, without `--profile`. **Expected:** the command prints the phases (copying the Windows files, splitting `install.wim` when it is larger than 4 GiB, writing autounattend.xml), a percentage every 10 %, `The installation media is ready in ...` and the reminder to run `formatkit prepare check`; exit code **0**.
2. Create the VM, start it and watch Setup.

**Expected:** Setup starts without a language screen (the language, keyboard and time zone come from the host or the `setup` section). The only manual screens are **disk selection**, the **local account** (name and password, typed by you in the VM console) and, because a VM has no firmware key, the **edition** screen. No license terms or privacy screens. After the first logon: `Test-Path C:\FormatKit` is **False** (no profile was bundled) and nothing starts by itself.

## 24. Media: profile with `importFile` and `networkDriverBackup`

1. On the host make a profile outside `.vm\media\` with a `programs.importFile` (`formatkit programs export`) and a `drivers.networkDriverBackup` folder (a folder with an `.inf`), using relative paths, plus `"setup": { "account": "local" }`. Build with `--profile`.

   **Expected:** exit code **0**; `.vm\media\out\sources\$OEM$\$1\FormatKit` holds `formatkit.exe`, `profile.json` and `inputs\1-...`, `inputs\2-...`; in the copied `profile.json` the two paths read `inputs\1-...` and `inputs\2-...`. A path used twice in the profile is copied once.
2. Create the VM, start it, finish Setup (disk, local account, edition) and sign in.

**Expected:** `Test-Path C:\FormatKit\formatkit.exe` is **True**. A visible console opens at the first logon with `Starting the profile '...' in 60 s (press Esc to cancel)...` counting down; press **Esc** in one run (**Expected:** `Automatic start cancelled.`, the command to run later, and the window closes after Enter; nothing ran) and let it run in another (restore or reinstall). The run starts after the countdown, reads `C:\FormatKit\inputs\`, asks for UAC if the first logon token is not elevated (record which happened), and writes logs and the report under `%ProgramData%\FormatKit\logs`. It resumes after reboots as scenario 4 does. `C:\FormatKit` stays afterwards.

## 25. Media: `account: "microsoft"`

Build with a profile whose `setup.account` is `"microsoft"` and install in the VM.

**Expected:** the online account screens of OOBE show (the sign-in with a Microsoft account is not completed); with `"local"` (scenarios 23 and 24) they do not. Record whether the local account screen appears offline on this build (spec section 6, item 1); if it does not, the fallback is the `BypassNRO` value.

## 26. Media: invalid input, nothing written

Run each on an empty `--output` and check the exit code (`$LASTEXITCODE`) and that the folder stays empty:

- `setup.edition` that the ISO does not have (for example `Windows 11 Education` on a consumer ISO): exit **2**, a message that names the editions in the image.
- `setup.language` the ISO does not have (an English ISO with `pt-BR`): exit **2**.
- A profile with a missing input (a `programs.importFile` that does not exist) and, separately, an unknown field `setup.foo`: exit **2**.
- A non-empty `--output`, a Windows 10 or ARM64 ISO, a folder that is not an ISO: exit **2**.
- `--output` on a drive without enough free space: exit **2** with the needed and free sizes.
- Decline the UAC prompt: exit **5**. Ctrl+C during the copy: exit **130**, the ISO is dismounted (`Get-DiskImage -ImagePath <iso>` shows `Attached : False`) and the folder is partial: delete it before the next try.

## 27. Media: `media usb` on your own USB stick

Run this one yourself, on the host, in an elevated PowerShell. **Use a spare stick with nothing you need: the chosen disk is erased.** Copy the ISO and the profile (and its inputs) to the host's internal drive first. Then report the output to Claude, who checks it against this list.

1. **Listing.** `artifacts\formatkit.exe media usb --iso C:\path\to\Win11.iso --profile C:\path\to\my-profile.json`, with the stick plugged in and no `--disk`.

   **Expected:** exit code **2**, the USB disks with number, model, size, `(USB, removable)`, drive letters and labels, and `Can be used.` for your stick. The boot and system disk are not listed. Nothing is erased. If your stick is listed as `not removable`, or shows up under hidden hard disks, record the exact model: the removable test (spec R7) needs a fix.
2. **Hidden external hard disk.** Plug in an external USB hard disk, run step 1 again.

   **Expected:** it is not listed; the line `1 external hard disk(s) are hidden. Use --allow-hard-disk to include them.` appears. With `--allow-hard-disk` it is listed (as not removable), still without erasing anything.
3. **Refusals.** Put a copy of the ISO or the profile on the stick and run with its number (`--disk <n>`). **Expected:** `Disk <n> cannot be used: it holds files the media needs (...)`, exit **2**, nothing erased. Also try the number of your system disk and a number that does not exist: both exit **2**.
4. **Wrong typed number.** Run with `--disk <n>` for your stick. **Expected:** the disk line with `EVERYTHING on it will be erased.`, its volumes, and the prompt `Type the disk number (<n>) to erase it and write the media. Anything else cancels:`. Type a different number, or just Enter: `Cancelled. Nothing was erased.`, exit **2**, and the stick's files are still there.
5. **Success.** Run again and type the right number. **Expected:** the phases and percentages, then `The USB stick is ready. Boot the PC to format from it.`, exit **0**. In Explorer the stick is `FORMATKIT`, FAT32, about 32 GiB at most (the rest of a larger stick is unallocated), and `Get-Disk <n> | Get-Partition` shows one active partition with the MBR style. `sources` holds `install.swm` parts when the image was split. Record the size shown by `Get-Partition` against the stick's size (spec: partition is the disk size minus 1 MiB, up to 32 GiB).
6. **Boot.** Boot a UEFI PC (a spare one if possible; Secure Boot on) from the stick, choose the one-time boot menu entry. **Expected:** Windows Setup starts; the screens are the ones of scenario 23 and, with a profile, the first logon behaves as in scenario 24. Stop at the disk screen if the PC holds data you want to keep: this scenario does not need to install.

## Hardware-only checklist (cannot be verified in a VM)

Confirm on real machines and fix the code/docs if wrong:

- [ ] **Dell**: Dell Command | Update exit code **1** means a reboot is required (R17, unverified).
- [ ] **Lenovo**: the System Update command-line arguments and its exit codes (R17, unverified).
- [ ] **HP**: the `C:\SWSetup` path used by HP packages (R16, unverified).
- [ ] **Nvidia**: the Microsoft Store NVIDIA App (`msstore XP8CLZL93F5Z4P`) installs and launches.
- [ ] **AMD** and **Intel**: the GPU driver arrives through Windows Update (or the OEM tool) only.
- [ ] The published `formatkit.exe` runs on a clean machine without .NET installed, and the SmartScreen prompt behaves as documented.
- [ ] **Cleanup diagnosis, HPET**: disable the High Precision Event Timer (`ACPI\PNP0103`) in Device Manager, run `cleanup check`, expect it under "Breaks Windows features:" with an `Enable-PnpDevice` fix, run the fix and confirm the finding is gone (scenario 9; Hyper-V Gen 2 VMs have no HPET).
- [ ] **Cleanup on Windows 11 Home**: apply a `level: balanced` profile and confirm the policy-based privacy items (`privacy.advertising-id`, `privacy.tailored-experiences`, `privacy.diagnostic-data`) take effect in Settings and survive a reboot (scenario 9 (c); no Home VM yet).
- [ ] **USB stick** (scenario 27): `media usb` recognises a real removable stick, hides an external hard disk and boots a real PC.
- [ ] **Network fallback** (spare physical machine): back up its NIC driver with `drivers backup-network`, uninstall the NIC device together with its oem driver, set `networkDriverBackup` in the profile, then run. Expect the driver to be reinstalled via `pnputil`, connectivity to return and the run to continue.
