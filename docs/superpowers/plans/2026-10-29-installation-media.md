# Installation Media Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `formatkit media build` and `formatkit media usb`, which turn the official Windows 11 ISO and an optional profile into a folder or USB stick that installs Windows with an `autounattend.xml` and starts the profile run on the first logon; plus `run --countdown` and the `prepare` item `security.windows11-requirements`.

**Architecture:** New project `FormatKit.Media` (references only `FormatKit.Core`): a pure `UnattendBuilder`, a `ProfileBundle` that copies the profile and its `ExternalInputs` into `sources\$OEM$\$1\FormatKit`, a `MediaBuilder` that writes the ISO contents to a target folder (splitting a large `install.wim`), and a `UsbTarget` that picks and prepares the stick. The core gains four services at the end of `ServiceSet` (`IDiskImages`, `IWindowsImages`, `IDisks`, `IWindowsReadiness`) and a `StartCountdown`.

**Tech Stack:** .NET 10 `net10.0-windows`, TreatWarningsAsErrors, System.CommandLine 2.0.14, Serilog, xunit.v3, `System.Xml.Linq`, `System.Text.Json.Nodes`.

**Spec:** `docs/superpowers/specs/2026-10-29-installation-media-design.md`

## Global Constraints

- Nothing is written or erased before every check of spec section 5.1 has passed (`media usb`: before the typed confirmation and the re-check of 5.2).
- No password anywhere: the answer file never has `UserAccounts`, `AutoLogon` or a `Password` element; the `setup` section has no password field (D4).
- The answer file has no `DiskConfiguration` and no `InstallTo` (D3).
- `media usb` erases only a disk that passes rules 1-4 of spec 5.2 twice (before the prompt and after it, matched by `UniqueId` and number), after the user typed its number at a console; there is no `--yes` (D9).
- The stick: MBR, one active FAT32 partition of `min(disk size, 32 GB)`, label `FORMATKIT` (D8). `install.wim` larger than 4 GiB − 1 byte (4294967295) is split into 3800 MB parts; a larger `install.esd` is an error.
- Exit codes: 0 done; 1 failed while writing; 2 invalid arguments, ISO, profile or `setup`, missing input, not enough space, ineligible disk, refused confirmation; 5 UAC declined (existing `ExitCodes.ElevationDeclined`); 130 Ctrl+C.
- No localized tool text is parsed: PowerShell scripts emit JSON (`ConvertTo-Json -Compress`) with numeric or invariant values; PowerShell literals go through `PowerShellText.SingleQuote`; every value goes through `ProcessSpec.Arguments`.
- Code, identifiers and repo text in English; user-facing strings in `src/FormatKit.Media/Resources/Strings.resx` and `Strings.pt-BR.resx` (CLI strings in the CLI pair, core strings in the core pair), kept in sync and checked by a resources test.
- `ServiceSet`: new services are the last parameters, wired in `src/FormatKit.Cli/Services.cs` and `tests/FormatKit.Core.Tests/Fakes/TestServices.cs` (optional parameter, fake default).
- Safety (CLAUDE.md): tests never mount, split, format or erase on the host. Only `media build` may run on the host, writing under `.vm\media\` (user approval, 2026-10-29). `media usb` is never run by Claude on the host.
- TDD: the failing test first. Unit suite: `dotnet test -- --filter-not-trait "Category=Integration"`, green after every task.
- Commits: conventional style, ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Never push or merge.

## Review Focus

1. A profile stored on the very stick being written (`E:\profile.json` with the stick as `E:`), or the ISO on it: refused before the prompt, nothing erased. Test in Task 9.
2. A stick swapped between the listing and the typed confirmation (same number, different `UniqueId`), or unplugged: no erase. Test in Task 9 (rule re-check) and Task 4 (`PrepareUsbAsync` script compares `UniqueId` itself).
3. Non-English ISO and host: an English ISO with `setup.language: "pt-BR"` is an error; a Portuguese ISO built on an English host defaults to the image's language; languages come from `Get-WindowsImage`, never `dism.exe` text. Tests in Tasks 6 and 3.
4. Media Creation Tool ISOs (`install.esd`), multi-edition ISOs, N editions, and an ARM64 or Windows 10 ISO (refused, exit 2). Tests in Task 8.
5. Inputs that are folders with deep trees (dotfiles with `.git`, a driver backup) and two JSON paths pointing at the same file: copied once, both paths rewritten, size counted once. Test in Task 7.

## Plan rulings (decisions the spec leaves open, taken in away mode)

- **R1** `DriversModule` today uses `networkDriverBackup` as written, so a relative path resolves against the current directory (which differs in the elevated child and after a resume). Spec section 7 relies on every module resolving relative paths against the profile folder. Task 1 makes Drivers resolve a relative `networkDriverBackup` against the profile folder, like Programs and Dev; absolute paths are unchanged. Recorded in the spec.
- **R2** The `prepare` requirements item needs RAM, firmware type, Secure Boot and TPM, which no service reads. A fourth service `IWindowsReadiness` reads them in one PowerShell script (Task 13). Firmware type from `$env:firmware_type` (`UEFI`/`Legacy`, readable without elevation); Secure Boot from `HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State` `UEFISecureBootEnabled`; RAM from `Win32_ComputerSystem.TotalPhysicalMemory`; TPM `SpecVersion` from `Win32_Tpm` only when elevated; build from `CurrentBuild`.
- **R3** The first-logon countdown is cancelled with **Esc**, like the reboot countdown (`Reboot_Countdown` says "press Esc to cancel"), not "any key": a stray key during the desktop's first load must not cancel the run. Recorded in the spec.
- **R4** `media build` and `media usb` elevate right after argument parsing (before loading the profile), so the confirmation prompt and all work run in the elevated child window, which already waits for Enter on exit.
- **R5** Keyboard default: `HKCU\Keyboard Layout\Preload` value `1` (a KLID such as `00000416`), mapped through `HKCU\Keyboard Layout\Substitutes\<klid>` when present, written as `<last 4 hex of KLID>:<KLID>` (for example `0416:00010416`). Missing → the language's culture name (Setup accepts it).
- **R6** Image facts come from `Get-WindowsImage -ImagePath <p>` (list) and `Get-WindowsImage -ImagePath <p> -Index <i>` (`Architecture` numeric: 9 = amd64, 12 = arm64, 0 = x86; `Version` such as `10.0.26100.1742`; `Languages`; `DefaultLanguageIndex`). Windows 11 = major 10 and build ≥ 22000.
- **R7** A stick is "removable" when `Get-Disk`'s `BusType` is `USB` (7) and the matching `Win32_DiskDrive` has `MediaType` containing `Removable` **or** `Capabilities` containing 7 (supports removable media) with `MediaType` not containing `hard disk`. The VM cannot prove this; scenario 27 (user, real stick) confirms or corrects it, with a test.
- **R8** Generic installation keys (Microsoft's public "generic keys" for installing, not activating): Home `YTMG3-N6DKC-DKB77-7M9GH-8HVX7`, Home N `4CPRK-NM3K3-X6XXQ-RXX86-WXCHW`, Home Single Language `BT79Q-G7N6G-PGBYW-4YWX6-6F4BT`, Pro `VK7JG-NPHTM-C97JM-9MPGT-3V66T`, Pro N `2B87N-8KFHP-DKV6R-Y2C8J-PKCKT`, Pro for Workstations `DXG7C-N36C4-C4HTG-X4T3X-2YV77`, Pro for Workstations N `WYPNQ-8C467-V2W6J-TX4WX-WT2RQ`, Pro Education `8PTT6-RNW4C-6V7J2-C2D3X-MHBPB`, Education `YNMGQ-8RYV3-4PGQ3-C8XTP-7CFBY`, Education N `84NGF-MHBT6-FXBX8-QWJK7-DRR8H`, Enterprise `XGVPP-NMH47-7TTHJ-W3FW7-8HV2C`, Enterprise N `WGGHN-J84D6-QYCPR-T7PJ7-X766F`. Keyed by the image name with the `Windows 11 ` prefix (`Windows 11 Pro`).
- **R9** Progress: `MediaBuilder` reports through `IProgress<MediaProgress>`; the CLI prints one line per phase and a percentage line every 10 % of bytes copied.

---

## File Structure

```
src/FormatKit.Core/
  SystemAccess/IDiskImages.cs, PowerShellDiskImages.cs
  SystemAccess/IWindowsImages.cs, PowerShellWindowsImages.cs     WindowsImageInfo
  SystemAccess/IDisks.cs, PowerShellDisks.cs                     DiskInfo, DiskVolume
  SystemAccess/IWindowsReadiness.cs, PowerShellWindowsReadiness.cs  ReadinessInfo
  Running/StartCountdown.cs
  Steps/ServiceSet.cs                                            + 4 services
src/FormatKit.Drivers/DriversModule.cs, DriversOptions.cs        R1
src/FormatKit.Media/
  FormatKit.Media.csproj, Localization/Strings.cs, Resources/Strings(.pt-BR).resx
  Setup/SetupOptions.cs, SetupSectionParser.cs, GenericKeys.cs, MachineLocale.cs, SetupDefaults.cs
  Unattend/UnattendBuilder.cs
  Bundle/ProfileBundle.cs, JsonPathWriter.cs
  Building/InstallMedia.cs, MediaBuilder.cs, MediaRequest.cs, MediaProgress.cs, MediaException.cs
  Usb/UsbTarget.cs, UsbEligibility.cs
src/FormatKit.Prepare/Checks/Security/Windows11RequirementsCheck.cs
src/FormatKit.Cli/Commands/MediaCommands.cs, RunCommand.cs
tools/vm/New-MediaVm.ps1
tests/FormatKit.Media.Tests/                                     mirrors src layout
tests/FormatKit.Core.Tests/Fakes/FakeDiskImages.cs, FakeWindowsImages.cs, FakeDisks.cs, FakeWindowsReadiness.cs
```

---

### Task 1: Drivers resolves a relative `networkDriverBackup` against the profile folder (R1)

**Files:**
- Modify: `src/FormatKit.Drivers/DriversOptions.cs`, `src/FormatKit.Drivers/DriversModule.cs`
- Test: `tests/FormatKit.Drivers.Tests/ExternalInputsTests.cs`, the existing `DriversModule` step-creation tests

**Interfaces:**
- Produces: `DriversOptions.From(JsonElement section, string? baseDirectory)` (the old one-argument overload is removed; callers pass the base directory). `DriversModule.CreateSteps(section, baseDirectory)` and `ExternalInputs(section, baseDirectory)` both use it.

- [ ] **Step 1: Write the failing tests:** `networkDriverBackup: "net"` with base `E:\usb` → `ExternalInputs` gives `E:\usb\net`, and the `NetworkCheckStep` created by `CreateSteps(section, @"E:\usb")` gets `E:\usb\net`; an absolute `D:\net` stays `D:\net`; base `null` with a relative path → `Path.GetFullPath` (today's behavior).
- [ ] **Step 2:** Run `dotnet test tests/FormatKit.Drivers.Tests`. Expected: FAIL.
- [ ] **Step 3: Implement** with `Path.GetFullPath(path, baseDirectory)` when relative and a base exists; update the `<remarks>` in `DriversModule`.
- [ ] **Step 4:** Run the unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `fix(drivers): resolve a relative networkDriverBackup against the profile folder`.

### Task 2: Core services for ISO images and Windows images

**Files:**
- Create: `src/FormatKit.Core/SystemAccess/IDiskImages.cs`, `PowerShellDiskImages.cs`, `IWindowsImages.cs`, `PowerShellWindowsImages.cs`
- Create: `tests/FormatKit.Core.Tests/Fakes/FakeDiskImages.cs`, `FakeWindowsImages.cs`
- Modify: `src/FormatKit.Core/Steps/ServiceSet.cs`, `src/FormatKit.Cli/Services.cs`, `tests/FormatKit.Core.Tests/Fakes/TestServices.cs`, `src/FormatKit.Core/Timeouts.cs`
- Test: `tests/FormatKit.Core.Tests/SystemAccess/PowerShellDiskImagesTests.cs`, `PowerShellWindowsImagesTests.cs`

**Interfaces:**
- Produces:
  - `public interface IDiskImages { Task<string> MountAsync(string isoPath, CancellationToken ct); Task DismountAsync(string isoPath, CancellationToken ct); }` — `MountAsync` returns a root such as `E:\`; both throw `InvalidOperationException` (message with the exit code) on failure.
  - `public sealed record WindowsImageInfo(int Index, string Name, int Architecture, string Version, IReadOnlyList<string> Languages, int DefaultLanguageIndex);`
  - `public interface IWindowsImages { Task<IReadOnlyList<WindowsImageInfo>> ListAsync(string imagePath, CancellationToken ct); Task SplitAsync(string wimPath, string swmPath, int partSizeMb, CancellationToken ct); }`
  - `Timeouts.Media = TimeSpan.FromMinutes(30)` (split, prepare, copy-related calls).
  - `ServiceSet(..., ILicensing Licensing, IDiskImages DiskImages, IWindowsImages WindowsImages)`.
  - Fakes: `FakeDiskImages` (`Root` to return, `Mounted`/`Dismounted` lists, `ThrowOnMount`, `ThrowOnDismount`); `FakeWindowsImages` (`Images` dictionary by path, `Splits` list of `(wim, swm, sizeMb)`, `ThrowOnSplit`, and on split writes the `.swm` names into a given `InMemoryFileSystem` when one is passed to its constructor).
  - `TestServices.Create(..., FakeDiskImages? diskImages = null, FakeWindowsImages? windowsImages = null)`.

- [ ] **Step 1: Write the failing tests** (with `FakeProcessRunner` returning canned output)
  - Mount: stdout `E` → `E:\`; the script has `Mount-DiskImage -ImagePath` with the path through `PowerShellText.SingleQuote` (a path with `'` is quoted); exit 1 → `InvalidOperationException`; empty stdout → `InvalidOperationException`.
  - Dismount: the script has `Dismount-DiskImage -ImagePath`; exit 1 → throws.
  - List: stdout `[{"Index":1,"Name":"Windows 11 Home","Architecture":9,"Version":"10.0.26100.1742","Languages":["pt-BR"],"DefaultLanguageIndex":0},{"Index":6,...}]` → two infos; a single object (PowerShell collapses one) → one info; `Languages` as a single string → one-item list.
  - Split: arguments include `Split-WindowsImage -ImagePath`, `-SplitImagePath`, `-FileSize 3800`, `-CheckIntegrity` absent; timeout is `Timeouts.Media`.
- [ ] **Step 2:** Run the two classes. Expected: FAIL.
- [ ] **Step 3: Implement.** Pattern of `PowerShellVolumes` (`powershell.exe -NoProfile -NonInteractive -Command <script>`, `$ErrorActionPreference = 'Stop'`, JSON out). Mount script: `$i = Mount-DiskImage -ImagePath <p> -StorageType ISO -PassThru; ($i | Get-Volume).DriveLetter`; when the image is already mounted, read the letter of the existing mount instead of failing. List script: for each `Get-WindowsImage -ImagePath <p>`, call `Get-WindowsImage -ImagePath <p> -Index $_.ImageIndex` and emit `Index, Name (ImageName), Architecture ([int]), Version, Languages, DefaultLanguageIndex` (R6).
- [ ] **Step 4:** Run the unit suite (all projects compile against the new `ServiceSet`). Expected: PASS.
- [ ] **Step 5:** Commit `feat(core): disk image and windows image services`.

### Task 3: Core service for disks

**Files:**
- Create: `src/FormatKit.Core/SystemAccess/IDisks.cs`, `PowerShellDisks.cs`, `tests/FormatKit.Core.Tests/Fakes/FakeDisks.cs`
- Modify: `ServiceSet.cs`, `Services.cs`, `TestServices.cs`
- Test: `tests/FormatKit.Core.Tests/SystemAccess/PowerShellDisksTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record DiskVolume(char? Letter, string? Label);`
  - `public sealed record DiskInfo(int Number, string UniqueId, string FriendlyName, long SizeBytes, string BusType, bool IsRemovable, bool IsBoot, bool IsSystem, IReadOnlyList<DiskVolume> Volumes);` (`BusType` is the invariant enum name, for example `USB`, `NVMe`, `SATA`)
  - `public interface IDisks { Task<IReadOnlyList<DiskInfo>> ListAsync(CancellationToken ct); Task<string> PrepareUsbAsync(int number, string uniqueId, CancellationToken ct); }`
  - `public sealed class DiskChangedException(string message) : Exception(message);` thrown by `PrepareUsbAsync` when the script finds another `UniqueId` (exit code 4) before touching the disk.
  - `public const long PowerShellDisks.MaxPartitionBytes = 32L * 1024 * 1024 * 1024;`
  - `ServiceSet(..., IWindowsImages WindowsImages, IDisks Disks)`; `FakeDisks(params DiskInfo[] disks)` with `Prepared` list of `(number, uniqueId)`, `PrepareRoot` (default `F:\`), `BeforeSecondList` hook (lets a test change the list between calls), `ThrowOnPrepare`; `TestServices.Create(..., FakeDisks? disks = null)`.

- [ ] **Step 1: Write the failing tests**
  - List: canned JSON with a USB stick (`BusType` 7, removable) and the system NVMe disk → two `DiskInfo` with `BusType` `USB` and `NVMe`, `IsRemovable` per R7, volumes with letters and labels; a disk without partitions → empty `Volumes`.
  - Prepare: the script contains, in this order, a `UniqueId` comparison that `exit 4`s, `Clear-Disk -RemoveData -RemoveOEM -Confirm:$false`, `Initialize-Disk -PartitionStyle MBR`, `New-Partition` with `-Size` (min of disk size and `MaxPartitionBytes`) and `-IsActive -AssignDriveLetter`, `Format-Volume -FileSystem FAT32 -NewFileSystemLabel 'FORMATKIT'`; the disk number and unique id go through `PowerShellText.SingleQuote`/invariant formatting; exit 4 → `DiskChangedException`; other non-zero → `InvalidOperationException`; stdout `F` → `F:\`.
  - `Clear-Disk` of an already-RAW disk: the script runs `Initialize-Disk` only when `PartitionStyle` is not already `MBR` after clearing (Clear-Disk leaves RAW; covered by asserting `if ($d.PartitionStyle -eq 'RAW')` precedes `Initialize-Disk`).
- [ ] **Step 2:** Run the class. Expected: FAIL.
- [ ] **Step 3: Implement.** List script: `Get-Disk` joined with `Get-Partition | Get-Volume` and `Get-CimInstance Win32_DiskDrive` (by `Index` = disk number) for R7; `BusType` as `[string]` of the enum (invariant name). Use `Timeouts.Query` for list, `Timeouts.Media` for prepare.
- [ ] **Step 4:** Run the unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(core): disk listing and USB stick preparation service`.

### Task 4: Media project, `setup` section and generic keys

**Files:**
- Create: `src/FormatKit.Media/FormatKit.Media.csproj` (shape of `FormatKit.Prepare.csproj`, `InternalsVisibleTo` `FormatKit.Media.Tests`, `FormatKit.Cli.Tests`, `formatkit`), `Localization/Strings.cs` (copy of Prepare's, namespace `FormatKit.Media.Localization`), `Resources/Strings.resx`, `Strings.pt-BR.resx`
- Create: `Setup/SetupOptions.cs`, `Setup/SetupSectionParser.cs`, `Setup/GenericKeys.cs`
- Create: `tests/FormatKit.Media.Tests/FormatKit.Media.Tests.csproj` (shape of the Prepare tests project); `dotnet sln add` both; reference Media from `FormatKit.Cli.csproj`
- Test: `tests/FormatKit.Media.Tests/Setup/SetupSectionParserTests.cs`, `GenericKeysTests.cs`, `ResourcesTests.cs`

**Interfaces:**
- Produces:
  - `public enum SetupAccount { Local, Microsoft }`
  - `public sealed record SetupOptions(SetupAccount Account = SetupAccount.Local, string? Edition = null, string? ComputerName = null, string? Language = null, string? Keyboard = null, string? TimeZone = null);`
  - `public static class SetupSectionParser { public static (SetupOptions? Options, IReadOnlyList<ProfileError> Errors) Parse(string profileJson); }` — reads the top-level `setup` of the profile text (comments and trailing commas allowed, as `ProfileLoader`); absent → `new SetupOptions()`; errors use `ProfileErrorCode.InvalidValue`/`UnknownField` (add `UnknownField` to the enum if absent; reuse the existing code modules use for unknown properties if there is one) with paths `setup`, `setup.account`, ...
  - `public static class GenericKeys { public static string? For(string editionName); public static IReadOnlyCollection<string> Editions { get; } }` (R8, case-insensitive).

- [ ] **Step 1: Write the failing tests**
  - Absent `setup` → defaults, no errors. Full valid example of spec 5.3 → every field set.
  - `setup` not an object → `setup` error. `account: "xyz"` → `setup.account`. `computerName`: `""`, 16 characters, `"PC_1"`, `"12345"` → `setup.computerName` errors; `"PC-JOAO"` ok. `timeZone: "Mars"` → `setup.timeZone` (validated with `TimeZoneInfo.FindSystemTimeZoneById`). `language: "xx-INVALID"` → `setup.language` (must be a `CultureInfo` name; image membership is checked later, Task 6). `keyboard: "0416:00010416"` and `"pt-BR"` ok; `"zz"` → `setup.keyboard` (pattern `^[0-9A-Fa-f]{4}:[0-9A-Fa-f]{8}$` or a culture name). `setup.foo` → unknown field. `setup.password` → unknown field (no password field exists).
  - `GenericKeys.For("Windows 11 Pro") == "VK7JG-NPHTM-C97JM-9MPGT-3V66T"`; `For("windows 11 pro")` same; `For("Windows 11 Ultimate")` null.
  - `ResourcesTests`: same names and placeholders in both resx files (copy of the Prepare test).
- [ ] **Step 2:** Run `dotnet test tests/FormatKit.Media.Tests`. Expected: FAIL (build errors).
- [ ] **Step 3: Implement** the types above; messages through the Core `ProfileError_*` strings where an equivalent exists, otherwise new Media strings.
- [ ] **Step 4:** Run the unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(media): project, setup section parser and generic keys`.

### Task 5: Answer file builder

**Files:**
- Create: `src/FormatKit.Media/Unattend/UnattendBuilder.cs`, `src/FormatKit.Media/Setup/ResolvedSetup.cs`
- Test: `tests/FormatKit.Media.Tests/Unattend/UnattendBuilderTests.cs`

**Interfaces:**
- Consumes: `SetupOptions`, `GenericKeys` (Task 4).
- Produces:
  - `public sealed record ResolvedSetup(SetupAccount Account, string? Edition, string? ComputerName, string Language, string Keyboard, string TimeZone);` (built by Task 6)
  - `public static class UnattendBuilder { public const string FirstLogonCommand = "cmd /c start \"FormatKit\" \"%SystemDrive%\\FormatKit\\formatkit.exe\" run --profile \"%SystemDrive%\\FormatKit\\profile.json\" --countdown 60"; public static string Build(ResolvedSetup setup, bool withProfile); }`

- [ ] **Step 1: Write the failing tests** (parse the result with `XDocument.Parse`, query by namespace `urn:schemas-microsoft-com:unattend`)
  - Three `settings` passes: `windowsPE`, `specialize`, `oobeSystem`; every component has `processorArchitecture="amd64"`.
  - `windowsPE` `International-Core-WinPE`: `SetupUILanguage/UILanguage`, `UILanguage`, `SystemLocale`, `UserLocale` = language; `InputLocale` = keyboard. `Microsoft-Windows-Setup/UserData/AcceptEula` = `true`.
  - Edition `Windows 11 Pro` → `ImageInstall/OSImage/InstallFrom/MetaData` with `Key` `/IMAGE/NAME` and `Value` `Windows 11 Pro`, and `UserData/ProductKey/Key` = the generic key with `WillShowUI` = `OnError`. No edition → no `ImageInstall`, no `ProductKey`.
  - `specialize`: `TimeZone` always; `ComputerName` only when set.
  - `oobeSystem` `OOBE`: `HideEULAPage`, `HideOEMRegistrationScreen` = `true`, `ProtectYourPC` = `3`; Local → also `HideOnlineAccountScreens` and `HideWirelessSetupInOOBE` = `true`; Microsoft → neither element present.
  - `withProfile` → `FirstLogonCommands/SynchronousCommand` with `Order` 1 and `CommandLine` = `FirstLogonCommand`; without → no `FirstLogonCommands`.
  - `Never_contains_secrets_or_disk_configuration`: for every combination of account × edition × profile, the text contains none of `Password`, `AutoLogon`, `UserAccounts`, `DiskConfiguration`, `InstallTo` (Global Constraints).
  - A computer name or edition with `&` or `<` is escaped (well-formed XML).
- [ ] **Step 2:** Run the class. Expected: FAIL.
- [ ] **Step 3: Implement** with `XDocument` (UTF-8 declaration, `wcm` namespace declared as in `tools/New-TestVm.ps1`).
- [ ] **Step 4:** Run the unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(media): autounattend.xml builder`.

### Task 6: Setup defaults and language resolution

**Files:**
- Create: `src/FormatKit.Media/Setup/MachineLocale.cs`, `SetupDefaults.cs`
- Test: `tests/FormatKit.Media.Tests/Setup/SetupDefaultsTests.cs`, `MachineLocaleTests.cs`

**Interfaces:**
- Consumes: `IRegistry`, `WindowsImageInfo` (Task 2), `SetupOptions` (Task 4).
- Produces:
  - `public sealed record MachineLocale(string UiLanguage, string? Keyboard, string TimeZone) { public static MachineLocale Current(IRegistry registry); }` (`CultureInfo.InstalledUICulture.Name`, R5, `TimeZoneInfo.Local.Id`)
  - `public static class SetupDefaults { public static (ResolvedSetup? Setup, string? Error) Resolve(SetupOptions options, MachineLocale machine, WindowsImageInfo image); }` — `Error` is a localized message naming the field (`setup.language`, `setup.edition`).

- [ ] **Step 1: Write the failing tests**
  - Language: `setup.language` `pt-BR` with image languages `["en-US"]` → error naming `setup.language` and listing `en-US`. No `setup.language`, machine `pt-BR`, image `["pt-BR","en-US"]` → `pt-BR`; machine `en-US`, image `["pt-BR"]` (default index 0) → `pt-BR`; comparison case-insensitive.
  - Keyboard and time zone: from `setup` when set, else from `MachineLocale`; machine keyboard null → the resolved language.
  - Edition: the image list is a single `WindowsImageInfo`; the edition check against all images is Task 8's job; `Resolve` copies `Edition` and `ComputerName` through.
  - `MachineLocale.Current` with `FakeRegistry`: Preload `1` = `00000416`, Substitutes `00000416` = `00010416` → `0416:00010416`; no Substitutes → `0416:00000416`; no Preload → null.
- [ ] **Step 2:** Run the classes. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4:** Run the unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(media): setup defaults from the current PC and the image`.

### Task 7: Profile bundle

**Files:**
- Create: `src/FormatKit.Media/Bundle/ProfileBundle.cs`, `Bundle/JsonPathWriter.cs`
- Test: `tests/FormatKit.Media.Tests/Bundle/ProfileBundleTests.cs`, `JsonPathWriterTests.cs`

**Interfaces:**
- Consumes: `LoadedProfile`, `ExternalInput`, `IFileSystem` (`EnumerateFileEntries`, `Copy`, `CreateDirectory`, `WriteAllTextAtomic`, `ReadAllText`, `FileExists`, `DirectoryExists`).
- Produces:
  - `public sealed record BundleItem(ExternalInput Input, string RelativePath, long Bytes);`
  - `public sealed class ProfileBundle { public static ProfileBundle Plan(LoadedProfile profile, IReadOnlyList<ExternalInput> inputs, IFileSystem files); public IReadOnlyList<BundleItem> Items { get; } public IReadOnlyList<string> Missing { get; } public long TotalBytes { get; } public void Write(string formatKitFolder, string exePath, IFileSystem files, IProgress<long>? copiedBytes, CancellationToken ct); }`
  - `internal static class JsonPathWriter { public static void Set(JsonNode root, string jsonPath, string value); }` (paths such as `modules.programs.direct[2].path`)

- [ ] **Step 1: Write the failing tests** (`InMemoryFileSystem`)
  - `Plan`: inputs `importFile` (file, 10 bytes) and `networkDriverBackup` (folder with two files, 5 + 7 bytes) → items `inputs\1-programs.json` and `inputs\2-netdrivers` (name = last segment of the resolved path), `TotalBytes` = 22 + profile size; a missing input → listed in `Missing` with its JSON path.
  - Two JSON paths with the same resolved path (case-insensitive) → one copy, both rewritten to the same relative path, bytes counted once.
  - `Write`: copies `formatkit.exe` from `exePath`, each input (folders recursively, keeping the tree), and `profile.json` whose JSON has `modules.programs.importFile` = `inputs\1-programs.json`, `modules.drivers.networkDriverBackup` = `inputs\2-netdrivers`, `modules.programs.direct[1].path` rewritten while `direct[0]` (a `url` item) is untouched; `setup` and every other property unchanged; comments in the original profile are dropped (JSON is re-serialized indented).
  - `Write` honors cancellation between files (`OperationCanceledException`).
  - `JsonPathWriter.Set` on a missing path throws `InvalidOperationException` (inputs only come from a validated profile).
- [ ] **Step 2:** Run the classes. Expected: FAIL.
- [ ] **Step 3: Implement.** Parse the original file text with `JsonNode.Parse` (`JsonDocumentOptions` with comments skipped and trailing commas allowed); the profile file itself is not an input here (it becomes `profile.json`).
- [ ] **Step 4:** Run the unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(media): bundle the profile and its input files`.

### Task 8: Install media check and media builder

**Files:**
- Create: `src/FormatKit.Media/Building/InstallMedia.cs`, `MediaRequest.cs`, `MediaProgress.cs`, `MediaException.cs`, `MediaBuilder.cs`
- Test: `tests/FormatKit.Media.Tests/Building/InstallMediaTests.cs`, `MediaBuilderTests.cs`

**Interfaces:**
- Consumes: `IDiskImages`, `IWindowsImages` (Task 2), `ResolvedSetup`/`SetupDefaults`/`MachineLocale` (Task 6), `UnattendBuilder` (Task 5), `ProfileBundle` (Task 7), `IVirtualDisks.FreeBytes`.
- Produces:
  - `public sealed class MediaException(string message) : Exception(message);` — validation failures (exit 2).
  - `public sealed record InstallImage(string Path, bool IsEsd, long Bytes, IReadOnlyList<WindowsImageInfo> Editions);`
  - `public static class InstallMedia { public static Task<InstallImage> InspectAsync(string root, ServiceSet services, CancellationToken ct); }` — throws `MediaException` when `sources\install.wim`/`.esd` or `efi\boot\bootx64.efi` is missing, an edition is not amd64 (9), or the version build is below 22000.
  - `public sealed record MediaRequest(string IsoPath, SetupOptions Setup, ProfileBundle? Bundle, MachineLocale Machine);`
  - `public sealed record PreparedMedia(string MountRoot, InstallImage Image, ResolvedSetup Setup, long RequiredBytes) : IAsyncDisposable;` — disposing dismounts (best effort; a failure is logged and printed with `Dismount-DiskImage -ImagePath '<iso>'`).
  - `public enum MediaPhase { Copying, Splitting, WritingFormatKit, WritingAnswerFile, Done }` and `public sealed record MediaProgress(MediaPhase Phase, long CopiedBytes, long TotalBytes);`
  - `public sealed class MediaBuilder(ServiceSet services, Serilog.ILogger log) { public Task<PreparedMedia> PrepareAsync(MediaRequest request, CancellationToken ct); public Task WriteAsync(PreparedMedia media, MediaRequest request, string targetRoot, IProgress<MediaProgress>? progress, CancellationToken ct); }`
  - `public const long MediaBuilder.Fat32MaxFile = 4294967295;` `public const int MediaBuilder.SplitPartMb = 3800;`

- [ ] **Step 1: Write the failing tests** (`InMemoryFileSystem` seeded as a mounted ISO at `E:\`, `FakeDiskImages.Root = "E:\\"`, `FakeWindowsImages`)
  - `InspectAsync`: valid tree → image with editions; no `install.wim`/`.esd` → `MediaException`; ARM64 (12) edition → `MediaException`; version `10.0.19045.1` → `MediaException` (Windows 10); `install.esd` preferred only when no `.wim`.
  - `PrepareAsync`: mounts; `setup.edition` not among the image names → `MediaException` naming `setup.edition` and the available names, and the ISO is dismounted; language error from `SetupDefaults` → `MediaException`, dismounted; `RequiredBytes` = ISO tree bytes + bundle bytes, plus 1 %.
  - `install.esd` larger than `Fat32MaxFile` → `MediaException`, dismounted.
  - `WriteAsync` to `G:\out`: copies every ISO file except the install image, preserving the tree; image of `Fat32MaxFile + 1` bytes → `SplitAsync(E:\sources\install.wim, G:\out\sources\install.swm, 3800)` and no `install.wim` in the target; image of exactly `Fat32MaxFile` → copied as is; with a bundle → `G:\out\sources\$OEM$\$1\FormatKit\formatkit.exe` and `profile.json` exist; `G:\out\autounattend.xml` equals `UnattendBuilder.Build(setup, withProfile)`; progress reports end with `Done` and `CopiedBytes == TotalBytes`.
  - Disposing `PreparedMedia` dismounts even after `WriteAsync` threw; a dismount failure does not hide the original exception.
- [ ] **Step 2:** Run the classes. Expected: FAIL.
- [ ] **Step 3: Implement.** Free space and folder emptiness are checked by the commands (Tasks 10-11), not here. Copy with `IFileSystem.EnumerateFileEntries(root, recursive: true)`, checking `ct` per file.
- [ ] **Step 4:** Run the unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(media): install media check and media builder`.

### Task 9: USB target

**Files:**
- Create: `src/FormatKit.Media/Usb/UsbEligibility.cs`, `Usb/UsbTarget.cs`
- Test: `tests/FormatKit.Media.Tests/Usb/UsbTargetTests.cs`

**Interfaces:**
- Consumes: `IDisks`, `DiskInfo`, `DiskChangedException` (Task 3).
- Produces:
  - `public enum UsbRefusal { None, NotUsb, BootOrSystem, HardDisk, HoldsSourceFiles, TooSmall }`
  - `public sealed record UsbCandidate(DiskInfo Disk, UsbRefusal Refusal, IReadOnlyList<string> SourcesOnDisk);`
  - `public static class UsbEligibility { public static UsbCandidate Check(DiskInfo disk, bool allowHardDisk, IReadOnlyList<string> sourcePaths, long requiredBytes); }` — rules 1-4 of spec 5.2 in that order; `sourcePaths` are the ISO, the profile file and every input; a path is on the disk when its drive letter is one of the disk's volume letters (UNC never); size rule uses `min(SizeBytes, MaxPartitionBytes)`.
  - `public sealed class UsbTarget(IDisks disks) { public Task<IReadOnlyList<UsbCandidate>> ListAsync(bool allowHardDisk, IReadOnlyList<string> sourcePaths, long requiredBytes, CancellationToken ct); public Task<string> PrepareAsync(UsbCandidate confirmed, bool allowHardDisk, IReadOnlyList<string> sourcePaths, long requiredBytes, CancellationToken ct); }` — `PrepareAsync` lists again, finds the disk with the same number, and throws `DiskChangedException` when it is gone, has another `UniqueId`, or now fails a rule; otherwise calls `IDisks.PrepareUsbAsync(number, uniqueId)` and returns the root.

- [ ] **Step 1: Write the failing tests**
  - Each rule: NVMe disk → `NotUsb`; USB boot disk (Windows To Go) → `BootOrSystem`; USB not removable → `HardDisk`, and `None` with `allowHardDisk`; stick `E:` with source `E:\profile.json` → `HoldsSourceFiles` listing that path (also for the ISO and an input folder); 8 GB stick with 9 GB required → `TooSmall`; 64 GB stick with 9 GB required → `None` (cap applied only to usable size); UNC source `\\nas\iso\win.iso` never matches.
  - `PrepareAsync`: `FakeDisks.BeforeSecondList` swaps the disk's `UniqueId` → `DiskChangedException`, `Prepared` empty; disk removed → same; profile copied onto the stick between the calls → same; unchanged → `Prepared == [(3, id)]` and the root returned.
- [ ] **Step 2:** Run the class. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4:** Run the unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(media): USB stick eligibility and re-check before erasing`.

### Task 10: `media build` command

**Files:**
- Create: `src/FormatKit.Cli/Commands/MediaCommands.cs`
- Modify: `src/FormatKit.Cli/CliApp.cs` (register; add `--iso` to `AbsolutizePaths`), CLI `Resources/Strings.resx` + `.pt-BR.resx`
- Test: `tests/FormatKit.Cli.Tests/MediaCommandsTests.cs`

**Interfaces:**
- Consumes: Tasks 4-8; `RunCommand.LoadProfile`; `CommandContext.EnsureElevatedAsync`; `IVirtualDisks.FreeBytes`.
- Produces: `internal static class MediaCommands { public static Command Create(CommandContext context); internal static Func<MachineLocale> Machine { get; set; } internal static Func<string?> ReadLine { get; set; } internal static Func<string> ExePath { get; set; } }` (test seams; defaults `MachineLocale.Current(registry)`, `Console.ReadLine`, `Environment.ProcessPath!`). The generated answer file is logged in full at Information level (spec 10). Shared private helper `PrepareAsync(context, iso, profilePath, ct)` that loads the profile, parses `setup` (prints errors as `Profile_Errors` does), collects inputs of enabled modules (like `PrepareCommands.CollectInputs`, without the profile file), plans the bundle (missing inputs → printed, exit 2) and calls `MediaBuilder.PrepareAsync` (`MediaException` → printed, exit 2).

- [ ] **Step 1: Write the failing tests** (pattern of `PrepareCommandsTests`; fakes from Tasks 2-3; `FakeElevation.IsElevated = true` unless stated)
  - Not elevated → `RelaunchElevatedAsync` called before any profile load or mount (R4); declined → exit 5.
  - Missing `--iso` or `--output` → exit 2 (System.CommandLine required options).
  - `--output` not empty → exit 2, nothing mounted. Not enough free space (`FakeVirtualDisks` free bytes) → exit 2, dismounted, nothing written.
  - Invalid profile → errors printed, exit 2, nothing mounted. `setup.account: "x"` → `setup.account` printed, exit 2. Missing input → its JSON path printed, exit 2.
  - Valid, no profile → exit 0; output folder has `autounattend.xml` without `FirstLogonCommands`; reminder line `Media_PrepareReminder` printed.
  - Valid with profile → exit 0; `$OEM$` folder present.
  - Split failure (`FakeWindowsImages.ThrowOnSplit`) → `Media_BuildIncomplete` printed (delete the folder and run again), exit 1, dismounted.
  - Ctrl+C during copy → exit 130 (handled by `CliApp`), dismounted.
- [ ] **Step 2:** Run `dotnet test tests/FormatKit.Cli.Tests --filter "FullyQualifiedName~MediaCommands"`. Expected: FAIL.
- [ ] **Step 3: Implement.** Strings: `Cmd_Media`, `Cmd_MediaBuild`, `Cmd_MediaUsb`, `Opt_MediaIso`, `Opt_MediaProfile`, `Opt_MediaOutput`, `Opt_MediaDisk`, `Opt_MediaAllowHardDisk`, `Media_OutputNotEmpty`, `Media_NotEnoughSpace`, `Media_MissingInput`, `Media_Phase_<MediaPhase>`, `Media_Percent`, `Media_BuildDone`, `Media_BuildIncomplete`, `Media_PrepareReminder`, `Media_DismountFailed`.
- [ ] **Step 4:** Run the unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(cli): media build command`.

### Task 11: `media usb` command

**Files:**
- Modify: `src/FormatKit.Cli/Commands/MediaCommands.cs`, CLI resx pair
- Test: `tests/FormatKit.Cli.Tests/MediaCommandsTests.cs`

**Interfaces:**
- Consumes: `UsbTarget`, `UsbEligibility` (Task 9), the helper of Task 10, `IInteractiveConsole.CanPrompt`, `MediaCommands.ReadLine`.

- [ ] **Step 1: Write the failing tests**
  - No `--disk` → table of all USB disks with their refusal reason (hidden external hard disks counted, with `Media_UsbHardDisksHidden` mentioning `--allow-hard-disk`), exit 2, `Prepared` empty, ISO dismounted.
  - `--disk 3` ineligible (each `UsbRefusal`) → its localized reason, exit 2.
  - `--disk 3` eligible: prompt shows number, model, size, bus, letters, labels and `Media_UsbEraseWarning`; `ReadLine` returns `"3"` → prepared and written, exit 0, `Media_UsbDone`; returns `"y"`, `""` or null → `Media_UsbCancelled`, exit 2, `Prepared` empty.
  - External hard disk with `--allow-hard-disk` → the prompt adds `Media_UsbIsHardDisk`.
  - `CanPrompt == false` → `Media_UsbNoConsole`, exit 2, `ReadLine` never called.
  - `DiskChangedException` from the re-check → `Media_UsbDiskChanged`, exit 2, nothing written.
  - Write failure after `PrepareUsbAsync` → `Media_UsbIncomplete`, exit 1.
  - `Never_prepares_without_typed_number`: across all the cases above, `FakeDisks.Prepared` is non-empty only in the two cases where `ReadLine` returned `"3"` (Review Focus 2; spec review focus 1).
- [ ] **Step 2:** Run the filter. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4:** Run the unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(cli): media usb command with typed confirmation`.

### Task 12: `run --countdown`

**Files:**
- Create: `src/FormatKit.Core/Running/StartCountdown.cs`; core resx pair (`Start_Countdown`, `Start_Cancelled`)
- Modify: `src/FormatKit.Cli/Commands/RunCommand.cs`, `src/FormatKit.Cli/CliEnvironment` (in `Services.cs`: a `StartCountdown` member), CLI resx pair (`Opt_Countdown`, `Run_CountdownUsage`)
- Test: `tests/FormatKit.Core.Tests/Running/StartCountdownTests.cs`, `tests/FormatKit.Cli.Tests/CliAppTests.cs` (run cases)

**Interfaces:**
- Produces: `public sealed class StartCountdown { public StartCountdown(); internal StartCountdown(Func<bool> escPressed, Func<TimeSpan, CancellationToken, Task> delay, Action<string> write); public Task<bool> WaitAsync(string profileName, TimeSpan countdown, CancellationToken ct); }` — true to proceed, false when Esc was pressed; same loop as `ConsoleRebooter.RebootAsync` (print every 10 s). `CliEnvironment` gains `StartCountdown? Countdown = null` (null → a real one).

- [ ] **Step 1: Write the failing tests**
  - `StartCountdown`: no key → true after the countdown, first line `Start_Countdown` with the profile name and `60`; Esc on the third poll → false and `Start_Cancelled`.
  - CLI: `run --profile p --countdown 60` with the fake countdown returning false → prints `Run_CountdownCancelled` with the command line `<exe> run --profile <p>` (paths quoted when they contain spaces), waits for one `ReadLine` when a console can prompt, exit 0, no plan built, no elevation. Returning true → the run proceeds as without the option. `--countdown` with `--resume` or `--dry-run` → `Run_CountdownUsage`, exit 2. `--countdown 0` → no wait. Negative → exit 2. `CanPrompt == false` → countdown skipped (R3 note in spec 8).
- [ ] **Step 2:** Run the two classes. Expected: FAIL.
- [ ] **Step 3: Implement.** The countdown runs before `RunFlow.ExecuteAsync`, so before elevation: the UAC prompt appears only after the countdown.
- [ ] **Step 4:** Run the unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(cli): run --countdown for the first logon`.

### Task 13: `prepare` item `security.windows11-requirements`

**Files:**
- Create: `src/FormatKit.Core/SystemAccess/IWindowsReadiness.cs`, `PowerShellWindowsReadiness.cs`, `tests/FormatKit.Core.Tests/Fakes/FakeWindowsReadiness.cs`
- Modify: `ServiceSet.cs`, `Services.cs`, `TestServices.cs`
- Create: `src/FormatKit.Prepare/Checks/Security/Windows11RequirementsCheck.cs`; Modify: `PrepareChecks.cs` (after `ActivationCheck`), Prepare resx pair
- Test: `tests/FormatKit.Core.Tests/SystemAccess/PowerShellWindowsReadinessTests.cs`, `tests/FormatKit.Prepare.Tests/Checks/Security/Windows11RequirementsCheckTests.cs`, `PrepareChecksTests.cs` (order)

**Interfaces:**
- Produces:
  - `public sealed record ReadinessInfo(int Build, long? RamBytes, bool? Uefi, bool? SecureBoot, int? TpmMajorVersion);` (`TpmMajorVersion` null when not elevated or no TPM readable)
  - `public interface IWindowsReadiness { Task<ReadinessInfo> ReadAsync(CancellationToken ct); }`
  - `ServiceSet(..., IDisks Disks, IWindowsReadiness Readiness)`; `FakeWindowsReadiness(ReadinessInfo info)` with `Throw`; default `new ReadinessInfo(26100, 16L << 30, true, true, 2)`.
  - `public sealed class Windows11RequirementsCheck : IPrepareCheck` with `CheckId = "security.windows11-requirements"`, group `Security`.

- [ ] **Step 1: Write the failing tests**
  - Service: canned JSON `{"Build":19045,"Ram":4294967296,"Firmware":"UEFI","SecureBoot":1,"Tpm":"2.0, 0, 1.38"}` → `(19045, 4 GiB, true, true, 2)`; `Firmware` `Legacy` → `Uefi` false; `SecureBoot` null → null; `Tpm` null → null; the script reads `Win32_Tpm` only inside an elevation check.
  - Check: build 22631 → one `Ok`. Build 19045 with all met and TPM 2 → `Ok`. RAM 2 GiB → `Attention` naming RAM; `Uefi` false → `Attention` naming UEFI; `SecureBoot` false → `Attention`; TPM 1 → `Attention`; TPM null and not elevated → an extra `NotChecked` with action `tpm.msc`. Every Windows 10 result's action mentions the PC Health Check app. Service throws → handled by the checklist (existing behavior).
- [ ] **Step 2:** Run the classes. Expected: FAIL.
- [ ] **Step 3: Implement** (R2). Strings `Win11_*` in both Prepare resx files.
- [ ] **Step 4:** Run the unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(prepare): warn when the PC does not meet Windows 11 requirements`.

### Task 14: VM tooling for the media scenarios

**Files:**
- Create: `tools/vm/New-MediaVm.ps1`

- [ ] **Step 1: Write the script.** Parameters `-MediaFolder <path>`, `-VmName FormatKit-Media`, `-Replace`. Packs the folder into `.vm\media\media.iso` with IMAPI2 (`New-IsoFile` from `tools/New-TestVm.ps1`, extended with a UEFI boot image from `<folder>\efi\microsoft\boot\efisys_noprompt.bin` through `IMAPI2.BootOptions` with `PlatformId = 0xEF`), so no key press is needed; creates a Generation 2 VM (Secure Boot `MicrosoftWindows`, vTPM, 4 GB RAM, 64 GB disk, Default Switch, DVD first); `-Replace` removes an existing VM of that name and its disk first (never `FormatKit-Test`: the script refuses that name). It does not start the VM (the user starts it and watches Setup).
- [ ] **Step 2:** Verify syntax only: `powershell -NoProfile -Command "[scriptblock]::Create((Get-Content tools/vm/New-MediaVm.ps1 -Raw)) | Out-Null"`. Expected: no error. (Running it needs the user's Hyper-V session; Task 16.)
- [ ] **Step 3:** Commit `chore(vm): script that boots a VM from a media folder`.

### Task 15: Documentation

**Files:**
- Modify: `README.md`, `profiles/example.json`, `docs/testing.md`, `docs/superpowers/specs/2026-10-29-installation-media-design.md`

- [ ] **Step 1:** README: both commands in the table; a "Make the installation media" section (what the stick contains, the manual screens, the `setup` section table, the safety rules of `media usb`, the 32 GB partition, Ventoy not supported, exit codes) linking "Before you format"; `run --countdown` in the `run` row; "Status" (installation media implemented; WPF UI planned) and the Portuguese summary.
- [ ] **Step 2:** `profiles/example.json`: a `setup` section with `"account": "local"` and comments for the optional fields (the loader allows comments).
- [ ] **Step 3:** `docs/testing.md`: "Installation media scenarios (23-27)" with spec section 11's scenarios: commands (`dotnet publish`, `artifacts\formatkit.exe media build --iso <iso> --output .vm\media\out [--profile ...]`, `tools\vm\New-MediaVm.ps1`), the screens expected, what to check in the VM afterwards (`Test-Path C:\FormatKit`, the countdown window, `%ProgramData%\FormatKit\logs`), and scenario 27's steps for the user's real stick (listing, a hidden external disk, wrong typed number, success, boot).
- [ ] **Step 4:** Spec: record R1 and R3 under "Decided in away mode" (drivers relative path; Esc instead of any key), and R2 (a fourth service). Commit `docs: installation media usage, VM scenarios and plan rulings`.

### Task 16: VM scenarios and findings

- [ ] **Step 1:** `dotnet publish src/FormatKit.Cli -c Release -o artifacts`. On the host (approved), scenario 26: `media build` with an edition and a language not in the ISO, and a missing input → exit 2, `.vm\media\` output folder not created. Then `media build` without and with a profile into `.vm\media\plain` and `.vm\media\profile`; inspect the folders (`autounattend.xml`, `$OEM$`, split parts).
- [ ] **Step 2:** Scenarios 23-25 need the user at the VM (Setup's disk and account screens, typing the account password): `tools\vm\New-MediaVm.ps1 -MediaFolder .vm\media\plain` and so on, per `docs/testing.md`. In away mode, leave them pending and list them in the closing message.
- [ ] **Step 3:** Scenario 27 is the user's (real stick on the host).
- [ ] **Step 4:** For each defect found: a failing unit test that pins it, the fix, the unit suite green, a commit `fix(media): ...`. Record the results in the spec's "Found while running" (including spec section 6's three items and R7), commit `docs: record what the media scenarios found`.
- [ ] **Step 5:** Ask the user before any push or merge.
