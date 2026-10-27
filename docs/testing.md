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

1. Restore `clean` and enable the network. Use a scenario that triggers `RebootRequired` (for example a Windows Update driver install that needs a restart).
2. Run `.\formatkit.exe run --profile example.json`.
3. When the reboot countdown appears, press **Esc**.
4. Check the task: `schtasks /query /fo LIST /v | findstr /i formatkit` (or Task Scheduler).
5. Run again until the countdown appears and let it finish.
6. After the VM reboots, log on and watch.

**Expected:**
- The countdown is displayed; Esc cancels it and the VM does not reboot; the resume scheduled task exists (trigger: at logon) and the run is left to resume at the next logon (exit code **4**, RebootPending).
- When the countdown completes the VM reboots; after logon the scheduled task starts `formatkit run --resume` in its own console window (the task passes the hidden `--wait-on-exit` flag, so the window waits for Enter at the end unless the exit code is 4) and the run continues from the next step instead of restarting.
- `.\formatkit.exe run --resume` with no interrupted run exits with code **6** (NothingToResume).

## 5. Report contents

Reports (`report.md` and `report.json`) are written only when a run finishes (completed or failed). A run that exits with code 4 (RebootPending) writes no report; the report appears after the post-reboot `--resume` run completes. Open the report of the last finished run (its folder is printed at the end of the run).

**Expected:** it contains the overall status, each step's title and outcome, the step messages, and a "next steps" list for steps that need manual action. No credentials or personal data.

## 6. Task removed at the end

After a run completes successfully (all steps done, no pending reboot):

```powershell
schtasks /query /fo LIST | findstr /i formatkit
```

**Expected:** no FormatKit scheduled task remains, and `run --resume` exits with code 6.

## Hardware-only checklist (cannot be verified in a VM)

Confirm on real machines and fix the code/docs if wrong:

- [ ] **Dell**: Dell Command | Update exit code **1** means a reboot is required (R17, unverified).
- [ ] **Lenovo**: the System Update command-line arguments and its exit codes (R17, unverified).
- [ ] **HP**: the `C:\SWSetup` path used by HP packages (R16, unverified).
- [ ] **Nvidia**: the Microsoft Store NVIDIA App (`msstore XP8CLZL93F5Z4P`) installs and launches.
- [ ] **AMD** and **Intel**: the GPU driver arrives through Windows Update (or the OEM tool) only.
- [ ] The published `formatkit.exe` runs on a clean machine without .NET installed, and the SmartScreen prompt behaves as documented.
- [ ] **Network fallback** (spare physical machine): back up its NIC driver with `drivers backup-network`, uninstall the NIC device together with its oem driver, set `networkDriverBackup` in the profile, then run. Expect the driver to be reinstalled via `pnputil`, connectivity to return and the run to continue.
