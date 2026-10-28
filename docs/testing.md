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
- [ ] **Network fallback** (spare physical machine): back up its NIC driver with `drivers backup-network`, uninstall the NIC device together with its oem driver, set `networkDriverBackup` in the profile, then run. Expect the driver to be reinstalled via `pnputil`, connectivity to return and the run to continue.
