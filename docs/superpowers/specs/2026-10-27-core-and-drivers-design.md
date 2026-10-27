# FormatKit — Core and Drivers Design

- **Date:** 2026-10-27
- **Status:** Draft, awaiting review
- **Scope:** Sub-project 1 (Core + profile) and sub-project 2 (Hardware and drivers)

## 1. Purpose

FormatKit helps a person reinstall Windows and end up with a **clean system**:
only what they need, current drivers, their chosen programs and a ready
development environment, with as few clicks as possible and a report of
anything left to do.

It does **not** migrate the old system. Programs are reinstalled fresh from a
list the user chose, and old settings are not restored. FormatKit only helps
the user avoid losing personal data before formatting.

### Audience

- Personal use first, then friends, then a public open-source project.
- Friends may not be developers, so defaults must be safe and anything risky
  must ask for confirmation.
- Target: Windows 11 x64.

### Success criteria for the whole product

The user formats the PC, plugs in the USB drive and ends with a clean Windows,
current drivers, their programs and their dev environment. A final report lists
everything that was done, skipped or failed, with a next action for each problem.

## 2. Product decomposition

The product is split into sub-projects. Each gets its own spec, plan and
implementation cycle.

| # | Sub-project | Delivers |
|---|---|---|
| 1 | Core + profile | Profile format, resumable step engine, logs, headless mode |
| 2 | Hardware and drivers | Hardware detection, drivers from Windows Update and OEM tools, GPU, missing-device report |
| 3 | Cleanup | Debloat and privacy tweaks in levels, each explained and reversible |
| 4 | Programs and dev environment | Program catalog (nothing preselected), winget search, WSL, Git/SSH, dotfiles, Dev Drive |
| 5 | Installation media | Official ISO + `autounattend.xml` + automatic run on first logon |
| 6 | Preparation and verification | "Safe to format?" checklist and final report |
| 7 | WPF interface | Screens on top of the core |

**This spec covers sub-projects 1 and 2.** The others are out of scope here.

## 3. Decisions already made

- **Stack:** C# on .NET 10, published as a single self-contained x64 `.exe`
  so it runs on a fresh Windows without installing .NET.
- **Architecture:** all steps are C# classes (approach A). Later data-heavy
  modules, such as cleanup tweaks, will be declarative JSON data interpreted by
  the C# core.
- **Language:** code and repository in English. User-facing text in English
  (default) and Brazilian Portuguese, chosen from the Windows display language.
- **License:** MIT.

## 4. Solution structure

```
formatkit/
├── src/
│   ├── FormatKit.Core/       profile, step engine, state, logging, system abstractions
│   ├── FormatKit.Drivers/    hardware detection and driver steps
│   └── FormatKit.Cli/        command-line executable
├── tests/
│   ├── FormatKit.Core.Tests/
│   └── FormatKit.Drivers.Tests/
├── profiles/example.json     example profile, no personal data
└── docs/
```

Rules:

- Each module is its own project and depends only on `FormatKit.Core`, never on
  another module.
- The operating system is reached only through interfaces in `FormatKit.Core`:
  - `IProcessRunner` runs external tools (`winget`, `pnputil`, `dism`) with a
    timeout and captures exit code, stdout and stderr.
  - `IHardwareInfo` reads hardware data through WMI.
  - `IFileSystem` handles file access.
  - `IWindowsUpdateClient` wraps the Windows Update Agent COM API.
  - `IScheduledTasks` registers and removes the resume task.
- User-facing strings live in `.resx` resources (`en` default, `pt-BR`).
- The WPF app (`FormatKit.App`) arrives in sub-project 7 and reuses the same core.

### CLI commands in this scope

```
formatkit hardware                                  show detected hardware
formatkit drivers scan                              report problem devices and available drivers (read-only)
formatkit drivers install [--dry-run]               install drivers (requires admin)
formatkit drivers backup-network --output <dir>     export physical network adapter drivers
formatkit run --profile <file> [--dry-run]          run the profile's steps
formatkit run --resume                              continue an interrupted run
```

## 5. Core

### 5.1 Profile

```json
{
  "schemaVersion": 1,
  "name": "My PC",
  "modules": {
    "drivers": {
      "enabled": true,
      "sources": ["windowsUpdate", "oem", "gpu"],
      "networkDriverBackup": "E:\\FormatKit\\drivers"
    }
  }
}
```

- `schemaVersion` lets future versions migrate older profiles. A profile with a
  newer `schemaVersion` than the app supports is rejected with a clear message.
- The profile is validated completely on load. An invalid profile never starts
  a run. Errors name the field and the problem, for example
  `modules.drivers.sources[2]: unknown value "xyz"`.
- Each module reads only its own section under `modules`. Unknown module
  sections are rejected.
- `profiles/example.json` ships with the app and holds no personal data.

### 5.2 Steps

Every step implements one interface:

| Member | Meaning |
|---|---|
| `Id` | Stable identifier, used in state and reports |
| `TitleKey` | Resource key for the translated title |
| `RequiresAdmin` | Whether the step needs elevation |
| `IsCritical` | If it fails, the run stops |
| `CheckAsync` | Returns whether the step's goal is already met; if so, the step is skipped |
| `ExecuteAsync` | Does the work and returns a `StepResult` |

`StepResult` is one of **Succeeded**, **Skipped**, **Failed** or
**RebootRequired**, with messages for the log and the report.

Steps must be **idempotent**: running a step again after success, failure or
interruption is safe.

### 5.3 Run engine

1. Load and validate the profile.
2. Each enabled module contributes its steps; the engine builds an ordered plan.
3. With `--dry-run`, print the plan and stop.
4. If any step requires admin and the process is not elevated, relaunch itself
   through UAC with the same arguments.
5. For each step: run `CheckAsync`, then `ExecuteAsync` if needed, then save state.
6. Write the report.

**State** lives in `%ProgramData%\FormatKit\state.json`: profile path and hash,
plan, and the result of each finished step. Writes are atomic (write a
temporary file, then rename).

**Reboots.** When a step returns `RebootRequired`:

1. Save state.
2. Register a scheduled task that runs `formatkit run --resume` at the next
   logon with highest privileges.
3. Show a cancelable 60-second countdown, then reboot.
4. On resume, continue from the first unfinished step. When the run ends,
   remove the scheduled task.

If the profile file changed since the run started (hash mismatch), `--resume`
stops and asks the user to start a new run.

**Failures.** A failed step does not stop the run unless it is critical. All
failures appear in the report. An interrupted step (Ctrl+C, power loss) has no
recorded result, so `--resume` runs it again.

### 5.4 Logs and report

- Detailed log file in `%ProgramData%\FormatKit\logs\run-<timestamp>.log`, and
  a short summary on the console, both through Serilog.
- Every external call logs the command, exit code, duration and output.
- At the end of a run: `report.json` (machine-readable) and `report.md`
  (human-readable) in `%ProgramData%\FormatKit\reports\<timestamp>\`, listing
  each step's result and a next action for every failure or leftover item.

## 6. Drivers module

### 6.1 Hardware detection (read-only, WMI)

| Data | Source |
|---|---|
| Manufacturer, model, system family | `Win32_ComputerSystem` |
| Motherboard manufacturer and product | `Win32_BaseBoard` |
| BIOS version | `Win32_BIOS` |
| GPUs and vendor | `Win32_VideoController`, vendor from PCI ID (`VEN_10DE` NVIDIA, `VEN_1002` AMD, `VEN_8086` Intel) |
| Problem devices | `Win32_PnPEntity` with `ConfigManagerErrorCode` ≠ 0 (code 28 = no driver), with hardware IDs |
| Physical network adapters and their driver | `Win32_NetworkAdapter` (`PhysicalAdapter = true`) and `Win32_PnPSignedDriver` |

The machine is classified as **OEM** (a brand with a supported update tool,
such as Dell, Lenovo or HP) or **custom build** (anything else, identified by
its motherboard).

### 6.2 Driver sources, in order

1. **Windows Update.** Search for drivers that are not installed
   (`IsInstalled=0 and Type='Driver'`) through the Windows Update Agent API,
   then download and install them. Report whether a reboot is required.
2. **OEM tool** (OEM machines only). Install the vendor's update tool through
   winget and run it silently: Dell Command Update, Lenovo System Update, HP
   Image Assistant.
   - **Custom builds:** install nothing. Motherboard utilities such as Armoury
     Crate are the bloat FormatKit avoids. The report links to the
     motherboard's support page instead.
3. **GPU** (only if the profile lists `gpu`). Install the vendor's official
   package through winget.
4. **Leftovers.** Every device still in error goes to the report with its
   hardware ID and a Microsoft Update Catalog search link.

The mapping from vendor to winget package ID, silent command line and support
URL lives in a JSON data file (`FormatKit.Drivers/Data/vendors.json`), not in
code. Exact package IDs and command lines are verified during implementation.

FormatKit never uses third-party "driver updater" programs.

### 6.3 Network driver fallback

- **Before formatting:** `formatkit drivers backup-network --output <dir>`
  exports the drivers of physical network adapters only, using
  `pnputil /export-driver` on each adapter's OEM INF.
- **After installing Windows:** the first drivers step, marked **critical**,
  checks connectivity. If there is none and `networkDriverBackup` is set, it
  installs drivers from that folder (`pnputil /add-driver <dir>\*.inf
  /subdirs /install`) and checks again. If there is still no connectivity, the
  run stops with instructions.

### 6.4 Steps contributed by the module

| Step | Critical | Admin |
|---|---|---|
| `drivers.network-check` | Yes | Yes |
| `drivers.windows-update` | No | Yes |
| `drivers.oem-tool` | No | Yes |
| `drivers.gpu` | No | Yes |
| `drivers.leftover-report` | No | No |

`drivers scan` and `--dry-run` only read the system. They are safe to run on
the current PC.

## 7. Error handling

- Every external call has a timeout (default 30 minutes for installs, 2
  minutes for queries) and logs its exit code and output.
- Known errors become clear messages with an action. Example: winget missing →
  "Update App Installer from the Microsoft Store."
- Windows Update errors are reported with their HRESULT and a short
  explanation for common codes.

## 8. Testing

- **Unit tests (xUnit)** for core and drivers, using fake implementations of
  the system interfaces. They never touch the real system. Example: "a Dell
  with code 28 on a network adapter produces a plan with the Dell Command
  Update step and the critical network step."
- **Read-only integration tests**, opt-in through a test trait: `hardware`
  and `drivers scan` against the real machine.
- **Install, reboot and resume tests** run only in a Hyper-V virtual machine,
  following a written script in `docs/testing.md`. Not automated in this phase.

## 9. Repository and delivery

- Public Git repository, MIT license.
- README in English with a short Portuguese summary and a warning that the
  app changes the system.
- GitHub Actions: build and unit tests on every push; publish the
  single-file `.exe` on release tags.
- Code signing (SignPath Foundation) is a future item, not in this scope.

## 10. Out of scope

Cleanup, programs and dev environment, installation media, the preparation
checklist and the WPF interface. Each gets its own spec.
