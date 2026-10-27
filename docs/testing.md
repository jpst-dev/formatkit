# Manual test script (Hyper-V)

FormatKit changes the system. Run this script in a disposable VM, never first on your main machine. Each item lists the expected result.

## Setup

1. Build or download `formatkit.exe` (`dotnet publish src/FormatKit.Cli -c Release -o artifacts`).
2. In Hyper-V create a **Generation 2** VM with a fresh **Windows 11** (at least 4 GB RAM, a virtual switch, TPM and Secure Boot enabled). Install Windows and finish OOBE with a local account.
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

**Expected (confirm):** exit code 0; the output folder contains one subfolder per physical network adapter that uses an OEM INF (adapters on inbox drivers are skipped), each with the exported `.inf`, `.sys` and `.cat` files. Confirm that running the command again also succeeds.

## 3. Run with a missing network driver (fallback and critical stop)

1. Restore checkpoint `clean`. Copy `formatkit.exe`, the profile and the backed-up `netdrivers` folder into the VM (for example to `C:\fk\netdrivers`).
2. Edit the VM copy of the profile so `modules.drivers` contains `"networkDriverBackup": "C:\\fk\\netdrivers"` (JSON needs doubled backslashes). The path must match the folder used by the backup step.
3. Simulate a missing network driver in the VM: in Device Manager, uninstall the network adapter and tick "Attempt to remove the driver for this device" (or run `pnputil /delete-driver <oemNN.inf> /uninstall /force`), so the VM has no connectivity. Do not merely disable the adapter: the fallback installs drivers but does not re-enable a disabled adapter.
4. Run `.\formatkit.exe run --profile example.json` and accept the UAC prompt.
5. Repeat from step 1 with the `networkDriverBackup` key removed.

**Expected, backup configured:** an elevated second window opens (UAC relaunch) and waits for **Enter** at the end. The network check step installs the backed-up drivers with `pnputil`, re-probes connectivity, and reports the network restored; the run continues. (If the machine is still offline after the install, the step fails with the "installed from ... but still no internet" message and the run exits with code **3**.)

**Expected, backup not configured:** the network check step fails with an actionable message (configure `networkDriverBackup`), steps that need the network are not attempted, and the run stops with exit code **3** (StoppedOnCriticalFailure). Restore the driver manually (or re-add `networkDriverBackup`) and run again to confirm the run proceeds.

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
- When the countdown completes the VM reboots; after logon the scheduled task starts `formatkit run --resume` in its own console window (this is not the UAC relaunch, so it never waits for Enter and closes when the process exits; read the result in the log or report) and the run continues from the next step instead of restarting.
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
- [ ] **AMD**: the GPU driver arrives through Windows Update only.
- [ ] The published `formatkit.exe` runs on a clean machine without .NET installed, and the SmartScreen prompt behaves as documented.
