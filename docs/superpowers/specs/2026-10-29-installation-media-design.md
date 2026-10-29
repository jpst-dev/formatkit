# FormatKit — Installation Media Design

- **Date:** 2026-10-29
- **Status:** Approved in away mode (2026-10-29): the user approved every
  design section in conversation, then left with the instruction to follow
  Claude's recommendations; the written spec itself awaits their morning
  review (see "Decided in away mode" at the end)
- **Scope:** Sub-project 5 (installation media): a Windows 11 installation
  folder or USB stick, built from the official ISO, that installs Windows with
  an `autounattend.xml` and starts FormatKit on the first logon
- **Builds on:** `2026-10-27-core-and-drivers-design.md` (profile loader, run
  engine, reboot and resume, sub-project table),
  `2026-10-29-preparation-checklist-design.md` (`IModule.ExternalInputs`,
  `prepare check`, which defers installation media here) and
  `2026-10-27-cleanup-design.md` (which deferred the Default User profile here)

## 1. Purpose

The core spec's success criterion is "the user formats the PC, plugs in the USB
drive and ends with a clean Windows". This sub-project makes that USB drive:
one stick that boots the official Windows 11 installer, answers its questions
and, after the first logon, runs the user's profile from `C:\FormatKit`.

FormatKit does not replace Rufus or the Media Creation Tool for everything they
do. It covers one case well: a UEFI Windows 11 x64 stick, made only with tools
that ship with Windows.

### Success criteria

- From the ISO and a profile, one command produces a stick that installs
  Windows with at most three manual screens (disk, local account and, without a
  firmware key, edition), then starts the profile run on the first logon with no
  further clicks.
- The command can never erase a disk the user did not clearly choose: never a
  fixed or system disk by default, never a disk holding the ISO, the profile or
  its inputs, never without typing the disk number.
- No password is written to the stick, the profile or the log.

## 2. Scope

**In 5:**

- A new project `FormatKit.Media` (section 4).
- Commands `formatkit media build` and `formatkit media usb` (section 5).
- An optional top-level profile section `setup` (section 5.3).
- `run --countdown <seconds>` (section 8).
- A new `prepare check` item, `security.windows11-requirements` (section 9).
- Three new system access services: `IDiskImages`, `IWindowsImages`, `IDisks`.

**Out of scope:**

- Downloading the ISO. Microsoft has no public API for it; the user downloads
  the official ISO and FormatKit checks it.
- Legacy BIOS/MBR boot, Linux ISOs, NTFS sticks with a third-party UEFI loader
  (Rufus's UEFI:NTFS), bad block checks.
- Bypassing the Windows 11 hardware requirements (TPM, Secure Boot, CPU, RAM).
  The `prepare` item warns before formatting instead (D7).
- Choosing or erasing the target disk automatically (D3). Future, in this
  order of preference: wipe automatically only when the machine has a single
  disk (needs a script in WinPE before disk configuration); a disk chosen in the
  profile (WinPE disk numbers differ from Windows and between machines).
- Applying per-user cleanup settings to the Default User profile
  (`C:\Users\Default\NTUSER.DAT`). It was deferred here by the cleanup spec, but
  it changes the cleanup engine and its undo, not the media, and it helps any
  install, not only one made by FormatKit. It becomes its own small cleanup
  change if wanted.
- Changing the Windows image (`dism /Mount-Image`, removing components).
- A data partition in the unused space of a stick larger than 32 GB.

## 3. Decisions

| # | Decision | Reason |
|---|---|---|
| D1 | Two layers: `media build` writes the media to a folder; `media usb` writes the same media to a stick it prepares. | A folder is easy to test and works with a VM, Rufus or a manual copy; the stick is the product's promise. Using another tool alone fails silently with Ventoy, which ignores `autounattend.xml` at the root. |
| D2 | A C# project with an `autounattend.xml` generator and the `sources\$OEM$` folder (approach 1). Not a PowerShell script (no tests, no localization, outside the exe, no future UI) and not offline image servicing (slow, needs scratch space, leaves mounted images on failure, and image changes are out of scope). | Follows the repository's conventions and gives one command to the end user. `$OEM$` is a documented Setup mechanism. |
| D3 | Setup shows its disk screen; the answer file has no `DiskConfiguration`. | Choosing the disk is the only decision that can destroy data, and the hardest to get right automatically ("disk 0" depends on enumeration order). The user is at the machine anyway: they just booted from the stick. |
| D4 | No password anywhere. `account: "local"` (default) hides the online account screens and Setup asks for the local account name and password; `account: "microsoft"` keeps the normal online screens. | Answer-file passwords are plain text (the "encoded" form is Base64) on a stick that travels; same rule as dev spec D3. |
| D5 | The edition is not set by default. With `setup.edition`, the answer file selects it and uses Microsoft's public generic installation key for it. | A firmware (OEM) key picks the right edition and reactivates by itself; forcing Pro on a Home license leaves Windows unactivated, which `security.activation` exists to prevent. |
| D6 | On the first logon, FormatKit starts on its own in a visible console with a cancelable 60-second countdown, once. | Whoever built the stick with a profile wants it to run; the countdown protects a stick used on the wrong PC or a user who only wanted a clean Windows. |
| D7 | No bypass of Windows 11 requirements; `prepare check` warns on the old PC instead. | The audience includes non-developers; an unsupported Windows 11 may stop getting security updates, and FormatKit promises a healthy system. |
| D8 | The stick gets one active FAT32 partition of `min(disk size − 1 MiB, 32 GiB)` on MBR; `install.wim` larger than 4 GiB − 1 byte is split into `.swm` parts. | UEFI firmware boots FAT32 with Secure Boot and no boot sector; Windows cannot format FAT32 above 32 GB; Setup reads split images natively. |
| D9 | `media usb` lists only removable USB disks by default; external USB hard disks need `--allow-hard-disk`. There is no `--yes`: erasing always needs the disk number typed at the console. | External hard disks are where people keep backups (Rufus hides them too). |
| D10 | The profile and every file it reads are copied into the media; the run never depends on the stick after Setup. | The stick may be removed or get another letter; reboots during the run resume from `C:`. |
| D11 | Media options live in the profile's `setup` section; the command line only takes what belongs to the media (`--iso`, `--output`, `--disk`, `--allow-hard-disk`). | The profile stays the single declarative description of the machine. |

## 4. Architecture

New project `src/FormatKit.Media`, referencing only `FormatKit.Core`. Tests in
`tests/FormatKit.Media.Tests`. It is not an `IModule`: it never runs inside a
profile (as `FormatKit.Prepare`).

| Unit | Responsibility |
|---|---|
| `SetupOptions` | Record: `Account` (`Local`, `Microsoft`), `Edition?`, `ComputerName?`, `Language?`, `Keyboard?`, `TimeZone?`. |
| `SetupSectionParser` | Reads and validates the `setup` element of the profile JSON. Returns `ProfileError`s with paths such as `setup.account`. |
| `SetupDefaults` | Fills missing language, keyboard and time zone from the current PC (through `IRegistry` and `TimeZoneInfo.Local`), then resolves the language against the image (section 6). |
| `GenericKeys` | The table of public generic installation keys by edition name (Home, Home N, Pro, Pro N, Education, Education N, Pro Education, Pro for Workstations, Enterprise). |
| `UnattendBuilder` | Pure function: resolved `SetupOptions`, the image's language and whether a profile is bundled → the `autounattend.xml` text (section 6). |
| `ProfileBundle` | From the loaded profile and the modules' `ExternalInputs`, copies each input to `FormatKit\inputs\<n>-<name>` and writes `profile.json` with those paths relative (section 7). |
| `InstallMedia` | Checks that a mounted ISO root is Windows 11 x64 installation media and reads its image: `sources\install.wim` or `install.esd`, its editions and languages, and `efi\boot\bootx64.efi`. |
| `MediaBuilder` | Writes the media to a target folder: copies the tree except the install image, splits or copies the image, writes `$OEM$` and `autounattend.xml`, reports progress. Mounts and always dismounts the ISO. |
| `UsbTarget` | Lists eligible disks, applies the rules of section 5.2, re-checks them before erasing, prepares the stick and returns its root. |
| CLI `Commands/MediaCommands` | `media build` and `media usb`: arguments, elevation, confirmation, output, exit codes. |

### 4.1 Core changes

New services, added as the last parameters of `ServiceSet`, wired in
`src/FormatKit.Cli/Services.cs` and given fakes in
`tests/FormatKit.Core.Tests/Fakes/TestServices.cs`. They use PowerShell and
CIM like `PowerShellVirtualDisks`, with literals through
`PowerShellText.SingleQuote` and JSON output; no localized tool text is parsed.

- `IDiskImages`:
  - `Task<string> MountAsync(string isoPath, CancellationToken ct)` returns the
    root (`E:\`) through `Mount-DiskImage` and `Get-Volume`.
  - `Task DismountAsync(string isoPath, CancellationToken ct)`.
- `IWindowsImages`:
  - `Task<IReadOnlyList<WindowsImageInfo>> ListAsync(string imagePath, CancellationToken ct)`
    returns `WindowsImageInfo(int Index, string Name, string Architecture,
    IReadOnlyList<string> Languages)` through `Get-WindowsImage` (name and
    index from the list; architecture and languages from `-Index`).
  - `Task SplitAsync(string wimPath, string swmPath, int partSizeMb, CancellationToken ct)`
    through `Split-WindowsImage`.
- `IDisks`:
  - `Task<IReadOnlyList<DiskInfo>> ListAsync(CancellationToken ct)` returns
    `DiskInfo(int Number, string UniqueId, string FriendlyName, long SizeBytes,
    string BusType, bool IsRemovable, bool IsBoot, bool IsSystem,
    IReadOnlyList<DiskVolume> Volumes)` with `DiskVolume(char? Letter,
    string? Label)`. Which property tells a removable stick from a USB hard
    disk without localized text is verified during implementation and pinned
    by tests, as the BitLocker values were for `prepare`.
  - `Task<string> PrepareUsbAsync(int number, string uniqueId, CancellationToken ct)`:
    fails without changing anything when disk `number` no longer has
    `uniqueId`; otherwise `Clear-Disk -RemoveData -RemoveOEM`, MBR, one active
    partition of `min(disk size − 1 MiB, 32 GiB)`, FAT32, label `FORMATKIT`, a drive
    letter; returns the root.
- Copying uses the existing `IFileSystem` (`EnumerateFileEntries`, `Copy`,
  `CreateDirectory`); free space uses the existing
  `IVirtualDisks.FreeBytes(root)`. No new file system method.
- `ProfileLoader` does not change: it already ignores unknown top-level keys,
  so `run` reads a profile with `setup` unchanged. Only `media` validates
  `setup`.

### 4.2 Other changes

- `run` gains `--countdown <seconds>` (section 8).
- `FormatKit.Prepare` gains the `security.windows11-requirements` check
  (section 9).

## 5. Commands

```
formatkit media build --iso <win11.iso> [--profile <file>] --output <folder>
formatkit media usb   --iso <win11.iso> [--profile <file>] [--disk <n>] [--allow-hard-disk]
```

- Without `--profile` the media installs Windows with the answer file
  (section 6) and runs nothing on the first logon: a plain Windows 11 stick.
- Both relaunch through UAC when not elevated, as `run` does (`Mount-DiskImage`
  and the image cmdlets need administrator rights).
- `media build`: `--output` must not exist or be empty. It may be on any
  drive, including `C:`. At a drive root, `System Volume Information` and
  `$RECYCLE.BIN` do not count (see "Decided in away mode").
- `media usb` without `--disk` lists the eligible disks (number, model, size,
  bus, current letters and labels) and exits 2. With no eligible disk it says
  so, mentioning `--allow-hard-disk` when an external hard disk was hidden.
- Exit codes: 0 done; 1 failed while writing; 2 invalid arguments, ISO, profile
  or `setup`, a missing input, not enough space, an ineligible disk, or a
  refused confirmation; 5 UAC declined; 130 Ctrl+C.
- Output language follows the Windows UI language (English and Brazilian
  Portuguese).

### 5.1 Flow

1. Validate the arguments and, with `--profile`, load the profile through
   `ProfileLoader` (an invalid profile prints the errors as `run` does), parse
   `setup`, and collect the `ExternalInputs` of every enabled module. A missing
   input is an error.
2. Mount the ISO; `InstallMedia` checks it and reads the image. Check
   `setup.edition` and the language against the image (section 6).
3. Compute the size needed (ISO contents + inputs + the exe + 1 % margin).
4. `media build`: check free space on the output drive. `media usb`: steps
   5.2.
5. Write the media (`MediaBuilder`), then dismount the ISO (always, in a
   `finally`).
6. Print the result: for `usb`, "The USB stick is ready. Boot the PC to format
   from it.", and in both cases a reminder to run `formatkit prepare check` on
   that PC first.

Nothing is written or erased before step 5 (or 5.2 step 4 for `usb`).

### 5.2 USB stick rules and confirmation

A disk is eligible when all hold:

1. Bus type USB, and neither the boot nor the system disk.
2. Removable media, unless `--allow-hard-disk`.
3. None of its volumes holds the ISO, the profile file, any input or the
   running `formatkit.exe`. The
   message tells the user to copy them elsewhere first (the profile often lives
   on the stick; copying it to a temporary folder and erasing would lose it if
   the write failed).
4. Its size fits the needed size (with the 32 GiB partition cap, measured on the usable size: disk size − 1 MiB).
5. Windows reports a `UniqueId` for it (see "Decided in away mode").

With `--disk <n>`:

1. Disk `n` not eligible: the reason, exit 2.
2. Print `Disk 3 - SanDisk Ultra 32 GB (USB, removable) - EVERYTHING on it will
   be erased.` with its current letters and labels; for an external hard disk
   add "This is an external hard disk."
3. Ask the user to type the disk number. Anything else cancels (exit 2). No
   console attached: refuse (exit 2).
4. List the disks again and re-check rules 1-5 for the disk with the same
   `UniqueId` and number; any difference stops without erasing.
5. `IDisks.PrepareUsbAsync`, then write the media to its root.

A failure or Ctrl+C after step 5 starts says the stick is incomplete and that
running the command again fixes it. There is no undo of the erase, which is why
everything that can be checked is checked before it.

### 5.3 Profile section `setup`

Optional, at the top level next to `modules`:

```json
"setup": {
  "account": "local",
  "edition": "Windows 11 Pro",
  "computerName": "PC-JOAO",
  "language": "pt-BR",
  "keyboard": "0416:00010416",
  "timeZone": "E. South America Standard Time"
}
```

| Field | Rule | Default |
|---|---|---|
| `account` | `local` or `microsoft`. | `local` |
| `edition` | An image name in the ISO and in `GenericKeys`. | none: firmware key or Setup's edition screen |
| `computerName` | 1-15 characters, letters, digits and `-`, not only digits. | none: Windows generates one |
| `language` | A culture name; must be a language of the image. | the current UI language if the image has it, else the image's default language (`DefaultLanguageIndex`) |
| `keyboard` | An input locale (`0416:00010416`) or a culture name. | the current PC's first keyboard layout |
| `timeZone` | A Windows time zone id (`TimeZoneInfo.FindSystemTimeZoneById`). | the current PC's time zone |

Unknown fields are errors (`setup.foo: unknown field`), as in module sections.
There is no password field and none will be added (D4).

## 6. The answer file

`UnattendBuilder` writes only what follows; everything else is Setup's normal
behavior. Component attributes are the amd64 ones used by `New-TestVm.ps1`.

**`windowsPE`:**

- `Microsoft-Windows-International-Core-WinPE`: `SetupUILanguage` and
  `UILanguage` = the resolved language; `InputLocale` = keyboard; `SystemLocale`
  and `UserLocale` = the resolved language (formats). A display language must
  be in the image (an English ISO has no Portuguese UI); keyboard and formats
  may be any.
- `Microsoft-Windows-Setup`:
  - `UserData/AcceptEula` = true.
  - With `setup.edition`: `ImageInstall/OSImage/InstallFrom/MetaData`
    `/IMAGE/NAME` = the edition, and `UserData/ProductKey/Key` = its generic
    key with `WillShowUI` `OnError`. Without it: neither, so the firmware key
    picks the edition, or Setup shows its key ("I don't have a product key")
    and edition screens.
  - No `DiskConfiguration` and no `InstallTo` (D3).

**`specialize`:** `Microsoft-Windows-Shell-Setup`: `TimeZone`; `ComputerName`
only when set.

**`oobeSystem`:**

- `Microsoft-Windows-International-Core`: the same language, keyboard and
  formats.
- `Microsoft-Windows-Shell-Setup/OOBE`: always `HideEULAPage`,
  `HideOEMRegistrationScreen` and `ProtectYourPC` = 3 (optional data collection
  off, in line with cleanup). With `account: "local"` also
  `HideOnlineAccountScreens` and `HideWirelessSetupInOOBE`, so Setup shows its
  local account screen ("Who's going to use this device?").
- Never `UserAccounts`, `AutoLogon` or any `Password` element.
- Only with a profile: `FirstLogonCommands` with one `SynchronousCommand`:

  ```
  cmd /c start "FormatKit" "%SystemDrive%\FormatKit\formatkit.exe" run --profile "%SystemDrive%\FormatKit\profile.json" --countdown 60
  ```

  `start` detaches FormatKit, so the desktop finishes loading. Windows runs
  first logon commands once, on the first logon after OOBE. If the process is
  not elevated, `run` relaunches through UAC as it does today.

To be confirmed in the VM and recorded under "Found while running", each with
its fallback:

1. Current Windows 11 (build 26100 and later) shows the local account screen
   offline with `HideOnlineAccountScreens`. Fallback: add the `BypassNRO`
   registry value in `specialize`.
2. `sources\$OEM$\$1` is copied to `C:\` when Setup runs from the stick.
   Fallback: a `specialize` command that finds the media by a marker file
   (`FormatKit\media.id`) and copies the folder.
3. The first logon command runs with the administrator token. Fallback: none
   needed (the UAC relaunch); record which one happened.

## 7. Media layout and the bundled profile

```
<root>
├── autounattend.xml
├── boot\, efi\, bootmgr, bootmgr.efi, setup.exe ...   from the ISO
└── sources\
    ├── install.swm, install2.swm ...    or install.wim / install.esd as is
    ├── boot.wim ...                      from the ISO
    └── $OEM$\$1\FormatKit\               only with --profile
        ├── formatkit.exe                 the running executable
        ├── profile.json
        └── inputs\
            ├── 1-programs.json
            ├── 2-netdrivers\
            └── ...
```

- The image is split into 3800 MB parts only when larger than 4 GiB − 1 byte;
  otherwise it is copied. `install.esd` above that size cannot be split and is
  an error before anything is written (D8).
- `ProfileBundle` copies each `ExternalInput` (from Programs, Drivers and Dev,
  the same list `prepare check` uses) once, numbered in the order the distinct
  existing inputs are found (module order; a path used twice keeps its first
  number), and
  rewrites its JSON path in the copy to `inputs\<n>-<name>`, relative to
  `profile.json`. Modules already resolve relative paths against the profile's
  folder, so `run` needs no change. The rest of the profile, including
  `setup`, is copied unchanged.
- The executable copied is `Environment.ProcessPath` (the single-file exe).

## 8. First logon: `run --countdown`

`formatkit run --profile <file> --countdown <seconds>`:

- Before planning, prints `Starting the profile 'My PC' in 60 s (press Esc to
  cancel)...` and counts down, as the reboot countdown in `ConsoleRebooter`
  does (R3, see "Decided in away mode").
- Esc cancels: it prints the command to run later
  (`C:\FormatKit\formatkit.exe run --profile C:\FormatKit\profile.json`), waits
  for Enter and exits 0. Cancelling is not an error.
- Otherwise the run proceeds unchanged: elevation, reboots and resume through
  the existing scheduled task, logs and report in `%ProgramData%\FormatKit`.
  The elevated child does not count down again.
- Not valid with `--resume` or `--dry-run` (exit 2). With no console attached
  the countdown is skipped.
- `C:\FormatKit` stays after the run. The report line saying it can be deleted
  was dropped (see "Decided in away mode").

## 9. `prepare check`: `security.windows11-requirements`

| Reads | Result |
|---|---|
| One PowerShell script (`IWindowsReadiness`, R2) reads the Windows build (`CurrentBuild`), RAM (`Win32_ComputerSystem`), the firmware type (`$env:firmware_type`), Secure Boot (`Confirm-SecureBootUEFI`, falling back to `UEFISecureBootEnabled` under `HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State`) and, only when elevated, the TPM version (`Win32_Tpm`). | Already Windows 11 (build 22000 or later): `Ok`. Windows 10: `Attention` for each requirement read as failed (RAM below 3.5 GiB, since `TotalPhysicalMemory` reads under the installed 4 GB; not UEFI, Secure Boot off), "Windows 11 will not install on this PC as is"; TPM not readable without elevation: `NotChecked` with "run `tpm.msc`"; always a pointer to the PC Health Check app for the CPU. All readable requirements met: `Ok`. |

FormatKit keeps no list of supported CPUs.

## 10. Error handling

- Everything that can be checked is checked before writing (section 5.1):
  profile, `setup`, ISO (missing, not Windows 11 x64 media, `install.esd` too
  large), edition and language against the image, inputs, space, disk.
- The ISO is dismounted in a `finally`. If dismounting fails, the message gives
  `Dismount-DiskImage -ImagePath '<iso>'` to run by hand.
- Every external call uses a timeout and is logged with its exit code and
  output; `SplitAsync` and `PrepareUsbAsync` use a longer one (minutes).
- An interrupted `media build` leaves a partial folder; the message says to
  delete it and run again. FormatKit does not delete it.
- The log never holds a password (there is none) and the answer file is logged
  in full.

## 11. Testing

- **Unit (`tests/FormatKit.Media.Tests`, fakes only):**
  - `UnattendBuilder`: local and Microsoft account, with and without edition,
    with and without profile, every pass. One test asserts that no generated
    file contains `Password`, `AutoLogon`, `UserAccounts` or
    `DiskConfiguration`. The output is well-formed XML.
  - `SetupSectionParser`: every rule of section 5.3 with its error path.
  - `SetupDefaults`: language in and not in the image, keyboard and time zone
    defaults.
  - `ProfileBundle`: relative rewrite of each input kind, folders copied whole,
    a missing input.
  - `MediaBuilder`: split above the limit, copy below, ESD above the limit
    refused, dismount on failure, not enough space.
  - `UsbTarget`: each eligibility rule, the profile on the stick itself, a
    different `UniqueId` at the re-check.
  - CLI: exit codes 0, 1, 2; a wrong typed number; `--countdown` cancel exits 0.
- **Prepare tests:** each row of section 9.
- **Integration (trait `Integration`, read-only):** `IDisks.ListAsync` and
  `IWindowsImages.ListAsync` against a real ISO when one is configured.
- **VM scenarios** (`docs/testing.md`, numbered after the existing ones). The
  user approved running `media build` on the host, writing only under
  `.vm\media\` (2026-10-29): it mounts the ISO read-only and writes a folder.
  A new script in `tools/vm/` packs that folder into a UEFI-bootable ISO
  (`New-IsoFile` from `New-TestVm.ps1` plus the `efisys` boot image) and
  creates a second, disposable VM `FormatKit-Media`, so the test VM and its
  `clean` checkpoint are untouched.
  - 23: `media build` without a profile; install. Expected manual screens:
    disk, local account, and edition (no firmware key in a VM).
  - 24: with a profile using `importFile` and `networkDriverBackup`:
    `C:\FormatKit` exists, the countdown shows, the run starts and reads
    `inputs\`.
  - 25: `account: "microsoft"`: the online account screens show.
  - 26: edition and language not in the image, a missing input: exit 2,
    nothing written.
  - 27: `media usb` with a real spare USB stick, run by the user on the host
    (a VM does not see a stick as USB): listing, the hidden external hard disk,
    the typed confirmation, a stick that boots a real PC if one is available.
    Claude writes the steps and checks the output the user reports.

## 12. Documentation

- README: both commands in the table, a "Make the installation media" section
  linking "Before you format", the `setup` section, "Status" and the Portuguese
  summary.
- `profiles/example.json`: a commented `setup` section.
- This spec gets a "Found while running" section after the VM scenarios.

## 13. Review focus

1. The erase path: eligibility, the re-check by `UniqueId`, and that no code
   path reaches `PrepareUsbAsync` without the typed confirmation.
2. Non-English Windows and ISOs: languages read from the image, no parsing of
   `dism.exe` or `diskpart` text.
3. ISO variants: `install.esd` (Media Creation Tool), multi-edition ISOs,
   N editions, an ARM64 or Windows 10 ISO (refused).
4. Large profile inputs (direct installers, dotfiles with `.git`) and the
   space check.
5. The first logon on current Windows 11 builds (section 6, items 1-3).

## Decided in away mode

Decisions taken without the user, for review:

- The written spec was approved without the user reading it. Every section
  had been approved in conversation; two details were added only in the
  written spec: the `setup` field rules of section 5.3 (`computerName` 1-15
  characters, unknown fields are errors), and `--countdown` refused with
  `--resume` or `--dry-run`. Reason: both follow existing conventions (module
  sections reject unknown fields; the countdown only makes sense for a new
  run).
- R1: a relative `drivers.networkDriverBackup` resolves against the profile
  folder, like Programs and Dev. Reason: section 7 relies on every module
  resolving relative paths against the profile folder, and the current
  directory differs in the elevated child and after a resume.
- R2: `IWindowsReadiness` is a fourth new service (section 2 lists three). It
  reads RAM, firmware type, Secure Boot, TPM version and the build for
  `security.windows11-requirements`, which no existing service could read.
- R3: Esc, not any key, cancels the `run --countdown` countdown (section 8
  said "any key"). Reason: like the reboot countdown, and a stray key while
  the desktop loads for the first time must not cancel the run.
- The stick's partition is `min(disk size - 1 MiB, 32 GiB)`, not
  `min(disk size, 32 GB)` (sections 4.1, D8). Reason: a partition as large as
  the whole disk cannot fit after the MBR and alignment; 32 GiB is the cap.
  Scenario 27 confirms it on a real stick.
- `MediaWriteException` (exit 1) is thrown for every failure while writing the
  media; `MediaException` (exit 2) only for failures found before writing.
  Reason: section 5 gives exit 1 for "failed while writing" and exit 2 for
  invalid input, and the CLI needs the type to tell them apart.
- A profile input folder that cannot be read makes the bundle invalid (exit 2,
  nothing written) instead of being copied incompletely. Reparse points
  (symlinks, junctions) inside input folders are skipped, because following
  them risks loops and duplicates.
- `media usb` without `--disk` hides external hard disks and only prints how
  many are hidden (and `--allow-hard-disk`); it lists them only with the flag
  (section 5, D9).
- A failure or Ctrl+C while the stick is being erased or written reports an
  incomplete stick and says to run the command again (section 5.2). The same
  holds when listing the disks again fails at the re-check, even if nothing
  was erased yet: simpler than telling the two apart, at the cost of a
  slightly pessimistic message.
- The RAM threshold of `security.windows11-requirements` is 3.5 GiB, not 4 GB
  (section 9), because `Win32_ComputerSystem.TotalPhysicalMemory` reads below
  the installed memory (a 4 GB PC reads about 3.8 GiB). Summing
  `Win32_PhysicalMemory` was rejected as more code for a rare case; an
  occasional RAM warning on a 4 GB machine with a large reserve for the
  integrated GPU is accepted.
- The same item tells a missing TPM from an unreadable one: "no TPM" is
  reported only when the elevated query succeeds and finds none; a TPM that
  cannot be read (not elevated, the query failed) is Not checked with
  `tpm.msc`. Every Windows 10 result also points at the PC Health Check app
  for the processor, since FormatKit keeps no CPU list.
- The countdown is skipped when there is no console (`--countdown 0` skips it
  too), and the run proceeds. Reason: an unattended start must not wait for a
  key nobody can press.
- Section 5.3 and section 7 wording now match the code: the default language
  is the image's `DefaultLanguageIndex` (not its first language), and inputs
  are numbered in the order the distinct existing inputs are found (not
  "profile order").
- The report does not say that `C:\FormatKit` can be deleted (section 8): it
  was not implemented and is dropped. The README says the folder stays and can
  be deleted by hand once the run succeeds.
- The elevated child of `run --countdown` does not count down again: the
  parent counted down before the UAC prompt, and the child gets the same
  arguments (`--elevated-child` skips the countdown). Without it the user
  waited twice.
- The running `formatkit.exe` counts as a source file for rule 3 of section
  5.2: started from the target stick, the erase would remove it.
- A disk whose `UniqueId` is blank is refused (rule 5 of section 5.2, before
  the prompt), `UsbTarget` refuses a blank confirmed id, and the prepare
  script exits 4 on a blank id and compares ids case-sensitively (`-cne`)
  before any destructive cmdlet. Reason: two blank ids match, which defeats
  the swap check.
- `--output` at a drive root ignores `System Volume Information` and
  `$RECYCLE.BIN` (section 5, D1), so `--output E:\` works on a freshly
  formatted stick. Elsewhere a folder with those names still counts.

## Found while running

Host runs on 2026-10-29, with `Windows11_Client_x64_pt-br_26300_9457.iso`
(build 26300, pt-BR, six editions: Home, Home Single Language, Education,
Pro, Pro Education, Pro for Workstations).

- Scenario 26 (host): an edition the ISO lacks (the message lists the six
  editions), `language: en-US` on the pt-BR ISO, a missing `importFile`, an
  unknown `setup.foo`, a file that is not an ISO and a non-empty `--output`
  each exit 2 and write nothing; the ISO is dismounted afterwards. Not run
  here: a Windows 10 or ARM64 ISO (none on the host), a full drive, declining
  UAC (the host shell was already elevated) and Ctrl+C during the copy.
- **Fixed:** `Split-WindowsImage` failed with access denied (0x80070005) on
  every build: the cmdlet opens the source image for writing, which the
  read-only mounted ISO refuses (it worked on a writable copy). The split now
  runs `dism.exe /English /Split-Image`, which reads the source read-only;
  only its exit code is used. Pinned by `Split_RunsDismSplitImage`.
- **Fixed:** a handled write failure printed the English log line "Writing
  the media to ... failed" above the localized message, because it was
  logged at Error and the console shows Warning and above. Handled media
  failures now log at Information, as `prepare` does. Pinned by
  `AssertNothingLoggedToTheConsole` in four failure tests.
- After the fixes, `media build` into `.vm\media\plain`, `.vm\media\profile`
  and `.vm\media\space test` (a path with a space) exits 0 in about 30 s
  each. `install.wim` (7.4 GB) becomes `install.swm` (3800 MB) and
  `install2.swm` (3640 MB) and is not left behind; `efi\microsoft\boot\
  efisys_noprompt.bin` and `efi\boot\bootx64.efi` are there. With the profile,
  `sources\$OEM$\$1\FormatKit` holds `formatkit.exe`, `profile.json`,
  `inputs\1-netdrivers\fake.inf` and `inputs\2-winget.json`, and the copied
  profile points at `inputs\1-netdrivers` and `inputs\2-winget.json`.
- The answer files hold no password or product key. Language pt-BR comes from
  the host, keyboard `0416:00000416` matches the host's first `Preload`
  entry (R5), and the time zone is `E. South America Standard Time`.
  `FirstLogonCommands` appears only in the profile build.
- Minor, left as is: the copy prints 10 % and then jumps to 100 % after the
  split, because the image is copied as one step; the inner English text of
  a split failure ("Splitting the Windows image failed (exit 1)") still
  appears inside the localized "incomplete" message.
- **Risk for scenario 27:** on this host `Get-Disk` and `Get-Partition` return
  nothing while `Get-PhysicalDisk` and `Win32_DiskDrive` see the NVMe disk
  (a stale storage provider cache; `Update-HostStorageCache` usually refreshes
  it). `media usb` lists disks with `Get-Disk`, so on such a PC it would say
  no USB disk was found. Not changed here: the refresh changes host state and
  was not run in away mode. If scenario 27 shows it, run
  `Update-HostStorageCache` first, and consider calling it in the list script.
- Pending, need the user: scenarios 23-25 in the `FormatKit-Media` VM
  (`tools\vm\New-MediaVm.ps1 -MediaFolder .vm\media\plain` and `profile`),
  which confirm section 6's three items (the offline local account screen,
  the `$OEM$` copy, the first logon token), and scenario 27 on a real stick,
  which confirms R7 (what counts as removable). `New-MediaVm.ps1` itself has
  not run yet.
