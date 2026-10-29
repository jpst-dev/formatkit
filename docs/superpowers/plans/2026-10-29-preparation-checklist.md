# Preparation Checklist Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `formatkit prepare check [--profile <file>] [--output <file.md>]`, a read-only "safe to format?" checklist with the 13 checks of the spec.

**Architecture:** New project `FormatKit.Prepare` (references only `FormatKit.Core`) with one `IPrepareCheck` per check id, run by `PrepareChecklist` and printed by two pure renderers. The core gains `IModule.ExternalInputs`, two read-only `IFileSystem` methods, `RegistryKind.ExpandString`, and two services (`IVolumes`, `ILicensing`) at the end of `ServiceSet`.

**Tech Stack:** .NET 10 `net10.0-windows`, TreatWarningsAsErrors, System.CommandLine 2.0.14, Serilog, xunit.v3.

**Spec:** `docs/superpowers/specs/2026-10-29-preparation-checklist-design.md`

## Global Constraints

- The command only reads. No check writes to the registry, files, services or disks, and nothing asks for elevation (spec D1, D4).
- `Blocker` only for loss FormatKit can prove; what only the user can confirm is `Attention` (D5).
- Exit codes: 0 no `Blocker`; 1 any `Blocker`; 2 invalid arguments, invalid profile or failed `--output` write; 130 Ctrl+C (D6).
- Output and logs never contain Git remote URLs, product keys (full or partial), recovery keys or Wi-Fi passwords. `git remote -v` and `git config remote.*.url` are never run (D7).
- A path is lost when it is on `%SystemDrive%` or on a volume whose backing VHDX is on `%SystemDrive%`; UNC paths are never lost (D8).
- Code, identifiers and repo text in English; user-facing strings in `src/FormatKit.Prepare/Resources/Strings.resx` and `Strings.pt-BR.resx` (CLI strings in the CLI's resx pair), read through `Localization/Strings`. Both files stay in sync.
- Every external value goes on a command line through `ProcessSpec.Arguments`; PowerShell literals go through `PowerShellText.SingleQuote`. External calls use `Timeouts.Query`.
- `ServiceSet`: new services are the last parameters, wired in `src/FormatKit.Cli/Services.cs` and `tests/FormatKit.Core.Tests/Fakes/TestServices.cs` (optional parameter, fake default).
- TDD: the failing test comes first. Unit suite: `dotnet test -- --filter-not-trait "Category=Integration"`, green after every task.
- Commits: conventional style, ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Never push or merge.

## Review Focus

1. Large profiles with unreadable folders (`UnauthorizedAccessException` deep in `AppData`-like trees outside the skip list): the scan skips and counts them and still finishes. Test in Task 1 (file system) and Task 6 (user folders).
2. OneDrive Files On-Demand: files with `RecallOnDataAccess` (0x400000) or `Offline` do not count toward a folder's size. Test in Task 6.
3. Non-English Windows: Git output parsed under `LC_ALL=C`, BitLocker read as a numeric property, activation as numbers and channel ids; no display text is parsed. Tests in Tasks 3 and 7.
4. Repositories in odd states: worktree (`.git` is a file), detached HEAD, `safe.directory` refusal (exit 128, "dubious ownership"): found, and the refusal is `NotChecked` with the `git config --global --add safe.directory` hint. Test in Task 7.
5. A Dev Drive VHDX on `C:` mounted as `D:`: anything on `D:` is lost (repositories scanned, `networkDriverBackup` there is a `Blocker`). Tests in Tasks 4, 7 and 9.

## Plan rulings (decisions the spec leaves open)

- **R1** `User Shell Folders` and `ProfileList\*\ProfileImagePath` are `REG_EXPAND_SZ`, which `IRegistry.Read` rejects today. Add `RegistryKind.ExpandString`: `Read` returns the unexpanded text (`RegistryValueOptions.DoNotExpandEnvironmentNames`); `Write` keeps rejecting it (`NotSupportedException`), since nothing needs to write one. Prepare expands with `PrepareEnvironment.Expand`.
- **R2** `networkDriverBackup` is used as written by the drivers module (no base-directory resolution). `DriversModule.ExternalInputs` returns it with `Path.GetFullPath` (current directory), which is what the run does.
- **R3** "Profile has `dev.ssh`" means the `dev` section is enabled and has an `ssh` property; Prepare reads it from `LoadedProfile.Profile.Modules["dev"]` directly.
- **R4** `ExternalInputs` is called only for sections whose module `IsEnabled` returned true, after the profile validated.
- **R5** Git runs with environment `LC_ALL=C`, `GIT_TERMINAL_PROMPT=0`, `GIT_OPTIONAL_LOCKS=0` (no index refresh writes).
- **R6** BitLocker property `System.Volume.BitLockerProtection` maps 1→`On`, 2→`Off`, 3→`Encrypting`, 4→`Decrypting`, 5→`Suspended`, 6→`Locked`, anything else or null→`Unknown`. Verified in VM scenario 20; a mismatch found there is fixed with a test.
- **R7** Sizes print as binary units with one decimal (`1.2 GB`, `850.0 MB`, `12.0 KB`), using `CultureInfo.CurrentUICulture`.

---

## File Structure

```
src/FormatKit.Core/
  Profiles/ExternalInput.cs               ExternalInput, ExternalInputKind
  Profiles/IModule.cs                     + ExternalInputs default method
  SystemAccess/IFileSystem.cs             + FileEntry, EnumerateDirectories, EnumerateFileEntries
  SystemAccess/FileSystem.cs              + implementations
  SystemAccess/IRegistry.cs, WindowsRegistry.cs   + ExpandString
  SystemAccess/IVolumes.cs, PowerShellVolumes.cs
  SystemAccess/ILicensing.cs, CimLicensing.cs
  Steps/ServiceSet.cs                     + Volumes, Licensing
src/FormatKit.Programs/ProgramsModule.cs  + ExternalInputs
src/FormatKit.Drivers/DriversModule.cs    + ExternalInputs
src/FormatKit.Dev/DevModule.cs            + ExternalInputs
src/FormatKit.Prepare/
  FormatKit.Prepare.csproj, Localization/Strings.cs, Resources/Strings(.pt-BR).resx
  PrepareItem.cs          PrepareStatus, PrepareGroup, PrepareItem
  PrepareContext.cs       PrepareContext, PrepareEnvironment
  IPrepareCheck.cs
  WindowsVolume.cs
  PrepareChecklist.cs     PrepareChecklist, PrepareReport
  PrepareChecks.cs        the ordered list of the 13 checks
  Sizes.cs                R7 formatting
  Checks/Security/*.cs, Checks/Data/*.cs, Checks/Developer/*.cs, Checks/Apps/*.cs
  Git/GitRepoInspector.cs, Git/RepoState.cs
  Rendering/ConsoleRenderer.cs, Rendering/MarkdownRenderer.cs
src/FormatKit.Cli/Commands/PrepareCommands.cs
tests/FormatKit.Prepare.Tests/            mirrors src layout
tests/FormatKit.Core.Tests/Fakes/FakeVolumes.cs, FakeLicensing.cs (+ InMemoryFileSystem, FakeRegistry, TestServices)
```

---

### Task 1: Read-only directory and file enumeration

**Files:**
- Modify: `src/FormatKit.Core/SystemAccess/IFileSystem.cs`, `src/FormatKit.Core/SystemAccess/FileSystem.cs`
- Modify: `tests/FormatKit.Core.Tests/Fakes/InMemoryFileSystem.cs`, the `IFileSystem` stub in `tests/FormatKit.Cleanup.Tests/ItemApplierTests.cs`
- Test: `tests/FormatKit.Core.Tests/SystemAccess/FileSystemEnumerationTests.cs`, `tests/FormatKit.Core.Tests/Fakes/InMemoryFileSystemTests.cs` (create if absent)

**Interfaces:**
- Produces:
  - `public sealed record FileEntry(string Path, long Length, FileAttributes Attributes);`
  - `IEnumerable<string> IFileSystem.EnumerateDirectories(string dir, Action<string>? onUnreadable = null)`: direct children only; skips hidden, system and reparse-point folders; a folder that cannot be listed calls `onUnreadable(dir)` and yields nothing.
  - `IEnumerable<FileEntry> IFileSystem.EnumerateFileEntries(string dir, bool recursive, Action<string>? onUnreadable = null)`: never follows reparse points; each unreadable folder calls `onUnreadable` once and is skipped.
  - `InMemoryFileSystem.SeedEntry(string path, long length, FileAttributes attributes = FileAttributes.Normal)`, `SeedDirectory(string path, FileAttributes attributes = FileAttributes.Directory)`, `MarkUnreadable(string dir)`.

- [ ] **Step 1: Write the failing tests** (real file system under a temp folder, cleaned in `Dispose`)
  - `EnumerateDirectories_SkipsHiddenAndReparsePoints`: children `a`, `b` (hidden), `link` (junction to `a`, created with `Directory.CreateSymbolicLink` only if allowed, else `mklink /J` through `cmd`) → `["a"]`.
  - `EnumerateFileEntries_Recursive_ReportsLengthAndAttributes`: `x.txt` (3 bytes), `sub\y.bin` (5 bytes, ReadOnly) → two entries with those lengths; `y.bin` has `ReadOnly`.
  - `EnumerateFileEntries_DoesNotFollowJunctions`: a junction to a sibling folder is not entered (file count unchanged).
  - In-memory fake: `SeedEntry` + `MarkUnreadable("C:\\u\\locked")` → enumeration of `C:\u` skips `locked` and calls `onUnreadable("C:\\u\\locked")` once.
- [ ] **Step 2:** Run `dotnet test tests/FormatKit.Core.Tests --filter "FullyQualifiedName~Enumerat"`. Expected: FAIL (methods not defined).
- [ ] **Step 3: Implement** with `DirectoryInfo.EnumerateDirectories`/`EnumerateFiles` (`EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip = 0 }`), own recursion with an explicit stack, catching `UnauthorizedAccessException`, `IOException` and `SecurityException` per folder. Add the same members to the fake (directories are the implicit parents of seeded paths plus `SeedDirectory`) and to the Cleanup test stub (`throw new NotSupportedException()`).
- [ ] **Step 4:** Run the same filter, then the unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(core): read-only directory and file enumeration`.

### Task 2: REG_EXPAND_SZ reads and module external inputs

**Files:**
- Modify: `src/FormatKit.Core/SystemAccess/IRegistry.cs`, `WindowsRegistry.cs`, `tests/FormatKit.Core.Tests/Fakes/FakeRegistry.cs` (only if it switches on kind), every `switch` on `RegistryKind` the compiler flags
- Create: `src/FormatKit.Core/Profiles/ExternalInput.cs`; Modify: `src/FormatKit.Core/Profiles/IModule.cs`
- Modify: `src/FormatKit.Programs/ProgramsModule.cs`, `src/FormatKit.Drivers/DriversModule.cs`, `src/FormatKit.Dev/DevModule.cs`
- Test: `tests/FormatKit.Core.Tests/SystemAccess/WindowsRegistryTests.cs` (existing, add case), `tests/FormatKit.Programs.Tests/ExternalInputsTests.cs`, `tests/FormatKit.Drivers.Tests/ExternalInputsTests.cs`, `tests/FormatKit.Dev.Tests/ExternalInputsTests.cs`

**Interfaces:**
- Produces:
  - `RegistryKind.ExpandString` (R1).
  - `public enum ExternalInputKind { File, Folder }`
  - `public sealed record ExternalInput(string JsonPath, string ResolvedPath, ExternalInputKind Kind);`
  - `IReadOnlyList<ExternalInput> IModule.ExternalInputs(JsonElement section, string? baseDirectory) => [];`
  - JSON paths: `modules.programs.importFile`, `modules.programs.direct[<i>].path`, `modules.drivers.networkDriverBackup`, `modules.dev.dotfiles.path`.

- [ ] **Step 1: Write the failing tests**
  - Registry, without touching the host registry (CLAUDE.md safety rule): extract `internal static RegistryValue WindowsRegistry.ToValue(string path, string name, RegistryValueKind kind, object raw)` and test `ToValue(..., RegistryValueKind.ExpandString, "%USERPROFILE%\\x")` → kind `ExpandString`, data `%USERPROFILE%\x`; `Binary` still throws `NotSupportedException`. A read-only Integration test reads `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders` `Personal` and gets kind `ExpandString`.
  - Programs: section with `importFile: "old.json"` and `direct: [{name,url,sha256}, {name, path: "D:\\setups\\tool.msi"}]`, `baseDirectory: "E:\\usb"` → `[("modules.programs.importFile", "E:\\usb\\old.json", File), ("modules.programs.direct[1].path", "D:\\setups\\tool.msi", File)]`. Absolute `importFile` is kept as written.
  - Drivers: `networkDriverBackup: "E:\\net"` → `[("modules.drivers.networkDriverBackup", "E:\\net", Folder)]`; absent → empty.
  - Dev: `dotfiles: { path: "dots", files: [...] }`, base `E:\\usb` → `[("modules.dev.dotfiles.path", "E:\\usb\\dots", Folder)]`; `dotfiles.repo` → empty.
- [ ] **Step 2:** Run the four test classes. Expected: FAIL.
- [ ] **Step 3: Implement.** Each module reuses its own parser (`ProgramsOptions.Parse`, `DriversOptions.From`, `DevOptions.Parse`) so relative paths resolve exactly as the steps resolve them; `direct` indices count every entry, including `url` ones. Drivers follows R2.
- [ ] **Step 4:** Run the unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(core): expand-string registry values and module external inputs`.

### Task 3: Volumes and licensing services

**Files:**
- Create: `src/FormatKit.Core/SystemAccess/IVolumes.cs`, `PowerShellVolumes.cs`, `ILicensing.cs`, `CimLicensing.cs`
- Create: `tests/FormatKit.Core.Tests/Fakes/FakeVolumes.cs`, `FakeLicensing.cs`
- Modify: `src/FormatKit.Core/Steps/ServiceSet.cs`, `src/FormatKit.Cli/Services.cs`, `tests/FormatKit.Core.Tests/Fakes/TestServices.cs`
- Test: `tests/FormatKit.Core.Tests/SystemAccess/PowerShellVolumesTests.cs`, `CimLicensingTests.cs`

**Interfaces:**
- Produces:
  - `public enum BitLockerState { Off, On, Encrypting, Decrypting, Suspended, Locked, Unknown }`
  - `public sealed record VolumeInfo(char Letter, int? DiskNumber, BitLockerState BitLocker, string? BackingVhdx, bool? HasRecoveryPassword);`
  - `public interface IVolumes { Task<IReadOnlyList<VolumeInfo>> ListAsync(CancellationToken ct); }`
  - `public sealed record WindowsActivation(bool Activated, string? Channel);`
  - `public interface ILicensing { Task<WindowsActivation?> GetWindowsActivationAsync(CancellationToken ct); }` (null when no Windows product with a key is found)
  - `ServiceSet(..., IVirtualDisks VirtualDisks, IVolumes Volumes, ILicensing Licensing)`
  - `FakeVolumes(params VolumeInfo[] volumes)` with `Throw` property; `FakeLicensing(WindowsActivation? value)` with `Throw` property; `TestServices.Create(..., FakeVolumes? volumes = null, FakeLicensing? licensing = null)`, defaults: one volume `C` on disk 0, `Off`, no VHDX; activation `(true, "OEM:DM")`.

- [ ] **Step 1: Write the failing tests** (with `FakeProcessRunner` returning canned JSON)
  - `PowerShellVolumes_ParsesVolumes`: stdout `[{"Letter":"C","Disk":0,"BitLocker":1,"Vhdx":null,"Recovery":null},{"Letter":"D","Disk":null,"BitLocker":6,"Vhdx":"C:\\DevDrive\\dev.vhdx","Recovery":true}]` → `C` disk 0 `On`; `D` disk null `Locked`, VHDX path, recovery true.
  - Single object instead of array (PowerShell collapses one item) → one volume.
  - `BitLocker` values 2, 3, 4, 5, 0 and null map per R6.
  - Non-zero exit or invalid JSON → throws `InvalidOperationException` whose message has the exit code (the checklist turns it into `NotChecked`).
  - `CimLicensing_ParsesActivation`: `{"LicenseStatus":1,"ProductKeyChannel":"Retail"}` → `(true, "Retail")`; `LicenseStatus` 5 → `Activated == false`; empty stdout → null.
  - `CimLicensing_NeverSelectsKeys`: the script argument contains neither `PartialProductKey` in a `Select-Object` nor `OA3xOriginalProductKey` (the filter may use `PartialProductKey IS NOT NULL`).
- [ ] **Step 2:** Run the two classes. Expected: FAIL.
- [ ] **Step 3: Implement.**
  - `PowerShellVolumes`: one `powershell.exe -NoProfile -NonInteractive -Command` script, output `ConvertTo-Json -Compress`. Per `Get-Volume | Where-Object { $_.DriveType -eq 'Fixed' -and $_.DriveLetter }`: `Disk` from `Get-Partition -DriveLetter` (null on error); `BitLocker` from `(New-Object -ComObject Shell.Application).NameSpace("X:").Self.ExtendedProperty('System.Volume.BitLockerProtection')`; `Vhdx` from the partition's disk when `BusType` is `File Backed Virtual` (its `Location`), null on error; `Recovery` only when elevated, from `Get-CimInstance -Namespace root\cimv2\Security\MicrosoftVolumeEncryption Win32_EncryptableVolume` `GetKeyProtectors(3)` non-empty, else null. Each property is wrapped in `try { } catch { $null }`.
  - `CimLicensing`: `Get-CimInstance SoftwareLicensingProduct -Filter "ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL" | Select-Object -First 1 LicenseStatus, ProductKeyChannel | ConvertTo-Json -Compress`. `Activated` is `LicenseStatus == 1`.
  - Wire real adapters in `Services.cs`, fakes in `TestServices`.
- [ ] **Step 4:** Run the unit suite (all projects compile against the new `ServiceSet`). Expected: PASS.
- [ ] **Step 5:** Commit `feat(core): volume and licensing read services`.

### Task 4: Prepare project skeleton, WindowsVolume and checklist runner

**Files:**
- Create: `src/FormatKit.Prepare/FormatKit.Prepare.csproj` (same shape as `FormatKit.Dev.csproj`, `InternalsVisibleTo` `FormatKit.Prepare.Tests`, `FormatKit.Cli.Tests`, `formatkit`), `Localization/Strings.cs` (copy of the Dev one with namespace `FormatKit.Prepare.Localization`), `Resources/Strings.resx`, `Resources/Strings.pt-BR.resx`
- Create: `PrepareItem.cs`, `PrepareContext.cs`, `IPrepareCheck.cs`, `WindowsVolume.cs`, `PrepareChecklist.cs`, `Sizes.cs`
- Create: `tests/FormatKit.Prepare.Tests/FormatKit.Prepare.Tests.csproj` (same shape as the Dev tests project); add both projects to `FormatKit.sln` with `dotnet sln add`; reference Prepare from `FormatKit.Cli.csproj`
- Test: `tests/FormatKit.Prepare.Tests/WindowsVolumeTests.cs`, `PrepareChecklistTests.cs`, `SizesTests.cs`, `ResourcesTests.cs`

**Interfaces:**
- Consumes: `VolumeInfo` (Task 3), `ExternalInput` (Task 2).
- Produces:
  - `public enum PrepareStatus { Blocker, Attention, NotChecked, Ok }` and `public enum PrepareGroup { Security, Data, Developer, Apps }` (declaration order is sort order).
  - `public sealed record PrepareItem(string CheckId, PrepareGroup Group, PrepareStatus Status, string Title, string? Detail = null, string? Action = null);`
  - `public sealed record PrepareEnvironment(string UserProfile, string LocalAppData, string AppData, char SystemDrive, string ComputerName, Func<string, string> Expand) { public static PrepareEnvironment Current(); }`
  - `public sealed record PrepareContext(ServiceSet Services, PrepareEnvironment Env, WindowsVolume Windows, LoadedProfile? Profile, IReadOnlyList<ExternalInput> Inputs, bool IsElevated, Serilog.ILogger Log);`
  - `public interface IPrepareCheck { string Id { get; } PrepareGroup Group { get; } Task<IReadOnlyList<PrepareItem>> RunAsync(PrepareContext context, CancellationToken ct); }`
  - `public sealed class WindowsVolume(char systemDrive, IReadOnlyList<VolumeInfo> volumes) { public bool IsLost(string path); public IReadOnlyList<VolumeInfo> LostVhdxVolumes { get; } }`
  - `public sealed record PrepareReport(IReadOnlyList<PrepareItem> Items) { public bool HasBlockers { get; } }`
  - `public sealed class PrepareChecklist(IReadOnlyList<IPrepareCheck> checks) { public Task<PrepareReport> RunAsync(PrepareContext context, CancellationToken ct); }`
  - `internal static class Sizes { public static string Format(long bytes); }` (R7)
  - Resource naming: `<CheckName>_<Case>_Title|Detail|Action`, for example `ProgramsExport_OnWindows_Title`; shared: `Group_Security`, `Status_Blocker`, `Item_NotChecked_Title`, `Item_NotChecked_Detail`.

- [ ] **Step 1: Write the failing tests**
  - `WindowsVolume`: system `C`; `IsLost("c:\\x")` true; `"D:\\x"` false; `"\\\\srv\\share\\x"` false; volume `D` with `BackingVhdx = "C:\\DevDrive\\dev.vhdx"` → `IsLost("D:\\backup")` true and `LostVhdxVolumes` has `D`; VHDX on `E:` → `D:` not lost; relative path resolves against the current directory.
  - `PrepareChecklist`: a check that throws `IOException` yields one item `(CheckId, Group, NotChecked, Item_NotChecked_Title text)` and the next check still runs; the exception is logged (Serilog `InMemorySink` or a capturing logger like the cleanup tests); `OperationCanceledException` propagates. Items are sorted by status, then group, then check id, keeping a check's own item order. `HasBlockers` true with one `Blocker`.
  - `Sizes.Format(0) == "0 B"`, `1536 → "1.5 KB"`, `1288490189 → "1.2 GB"` (invariant culture in the test via `CultureInfo.CurrentUICulture` swap).
  - `ResourcesTests`: every name in `Strings.resx` exists in `Strings.pt-BR.resx` and vice versa, with the same `{n}` placeholders (pattern of the other projects' resource tests).
- [ ] **Step 2:** Run `dotnet test tests/FormatKit.Prepare.Tests`. Expected: FAIL (build errors).
- [ ] **Step 3: Implement** the types above. `PrepareEnvironment.Current()` uses `Environment.GetFolderPath` and `Environment.GetEnvironmentVariable("SystemDrive")`.
- [ ] **Step 4:** Run the unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(prepare): project, windows volume and checklist runner`.

### Task 5: Security checks (`security.bitlocker`, `security.activation`)

**Files:**
- Create: `src/FormatKit.Prepare/Checks/Security/BitLockerCheck.cs`, `ActivationCheck.cs`; strings in both resx
- Test: `tests/FormatKit.Prepare.Tests/Checks/Security/BitLockerCheckTests.cs`, `ActivationCheckTests.cs`

**Interfaces:**
- Consumes: `IVolumes`, `ILicensing` via `context.Services`; `PrepareContext`, `PrepareItem` (Task 4).
- Produces: `BitLockerCheck : IPrepareCheck` (Id `security.bitlocker`), `ActivationCheck : IPrepareCheck` (Id `security.activation`). A test helper `PrepareTestContext.Create(...)` in `tests/FormatKit.Prepare.Tests/PrepareTestContext.cs` that builds a `PrepareContext` from `TestServices.Create` (system drive `C`, user profile `C:\Users\joao`, not elevated, no profile) — every later check task uses it.

- [ ] **Step 1: Write the failing tests** (spec 6.1, one test per row case)
  - `C` `On` → one `Ok` mentioning that reinstalling turns device encryption on again.
  - `D` `On`, not elevated → `Attention` whose action contains `aka.ms/myrecoverykey` and `manage-bde -protectors -get D:`, plus one `NotChecked` whose action contains `manage-bde -protectors -get D:`.
  - `D` `On`, elevated, `HasRecoveryPassword == false` → `Attention` whose action contains `manage-bde -protectors -add D: -RecoveryPassword`; no `NotChecked`.
  - `D` `Off` → no item for `D`. `D` `Unknown` → `NotChecked`.
  - Activation `(true, "OEM:DM")` → `Ok`; `(true, "Retail")` → `Attention` mentioning Settings > System > Activation; `(true, "Volume:MAK")` → `Attention` (organization text); `(true, "OEM:NONSLP")` → Retail text; `(false, "Retail")` → `Attention` "not activated"; null → `NotChecked`.
- [ ] **Step 2:** Run the two classes. Expected: FAIL.
- [ ] **Step 3: Implement** both checks and their en/pt-BR strings.
- [ ] **Step 4:** Run the unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(prepare): bitlocker and activation checks`.

### Task 6: Personal data checks (`data.user-folders`, `data.other-users`, `data.volumes`)

**Files:**
- Create: `src/FormatKit.Prepare/Checks/Data/UserFoldersCheck.cs`, `OtherUsersCheck.cs`, `VolumesCheck.cs`; strings
- Test: `tests/FormatKit.Prepare.Tests/Checks/Data/*Tests.cs`

**Interfaces:**
- Consumes: `IRegistry` (`ExpandString`, Task 2), `IFileSystem.EnumerateFileEntries` (Task 1), `IUserEnvironment.Get("OneDrive")`, `IUserSession.CurrentUserSid`, `WindowsVolume`.
- Produces: `UserFoldersCheck` (`data.user-folders`), `OtherUsersCheck` (`data.other-users`), `VolumesCheck` (`data.volumes`).
- Registry values read: `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders` names `Desktop`, `Personal`, `My Pictures`, `My Video`, `My Music`, `{374DE290-123F-4565-9164-39C4925E467B}`; `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\<sid>` `ProfileImagePath`.

- [ ] **Step 1: Write the failing tests** (spec 6.2)
  - Documents at `%USERPROFILE%\Documents` with 2 files (1 MB + 1 MB) → `Attention` with `2.0 MB` and the copy action.
  - Pictures under the OneDrive folder (`OneDrive` = `C:\Users\joao\OneDrive`) → `Ok` "synced by OneDrive".
  - A 5 GB file with `RecallOnDataAccess` (`(FileAttributes)0x400000`) plus a 1 KB local file in Documents → size `1.0 KB`.
  - Desktop redirected to `D:\Desktop` → `Ok`. Empty Music → `Ok`. Missing registry value → falls back to `%USERPROFILE%\<default folder name>`.
  - An unreadable subfolder → the detail mentions "1 folder could not be read".
  - Other users: `ProfileList` has the current SID, `S-1-5-18`, and `S-1-5-21-...-1002` at `C:\Users\maria` (folder exists) → one `Attention` naming `maria`, with the size when every folder was readable and without it when any was not; a profile whose folder is missing → no item.
  - Volumes: `C` and `D` on disk 0, `E` on disk 1 → `Attention` for `D` (Setup text), `Ok` for `E`; any disk number null → one `NotChecked`; a volume in `LostVhdxVolumes` → no item.
- [ ] **Step 2:** Run the three classes. Expected: FAIL.
- [ ] **Step 3: Implement** the three checks and strings.
- [ ] **Step 4:** Run the unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(prepare): personal data checks`.

### Task 7: Git repository inspector and `dev.git-repos`

**Files:**
- Create: `src/FormatKit.Prepare/Git/RepoState.cs`, `GitRepoInspector.cs`, `src/FormatKit.Prepare/Checks/Developer/GitReposCheck.cs`; strings
- Test: `tests/FormatKit.Prepare.Tests/Git/GitRepoInspectorTests.cs`, `Checks/Developer/GitReposCheckTests.cs`

**Interfaces:**
- Consumes: `IFileSystem.EnumerateDirectories` (Task 1), `IProcessRunner`, `IToolLocator`, `WindowsVolume.LostVhdxVolumes`.
- Produces:
  - `public sealed record BranchAhead(string Name, int Commits);`
  - `public sealed record RepoState(string Path, int Uncommitted, IReadOnlyList<BranchAhead> Ahead, IReadOnlyList<string> NoUpstream, int Stashes, bool HasRemote) { public bool IsSafe { get; } }`
  - `public sealed class GitRepoInspector(IProcessRunner processes, IFileSystem files, string gitExe)` with `IReadOnlyList<string> Find(string root, int maxDepth, Action<string> onUnreadable)` and `Task<RepoState> InspectAsync(string repo, CancellationToken ct)` (throws `GitInspectionException(string message)` on a non-zero exit, a timeout, or dubious ownership, with `IsDubiousOwnership` set for the last).
  - `GitReposCheck` (`dev.git-repos`), `MaxDepth = 5`, skipped folder names `AppData`, `node_modules` (case-insensitive).
  - Git lookup: `IToolLocator.Find("git.exe", [@"%ProgramFiles%\Git\cmd\git.exe"])` (expanded).

- [ ] **Step 1: Write the failing tests**
  - `Find`: repos at `C:\Users\joao\src\a` (`.git` folder) and `C:\Users\joao\src\b` (`.git` file, a worktree); `C:\Users\joao\AppData\x\.git` and `node_modules\y\.git` not found; a repo at depth 6 not found; nothing is searched inside `a`.
  - `InspectAsync` arguments: four calls, each `["-C", repo, ...]` with `status --porcelain=v2 --branch`, `for-each-ref --format=%(refname:short)%09%(upstream:short)%09%(upstream:track,nobracket) refs/heads`, `stash list`, `remote`; environment per R5. No call contains `-v`, `get-url` or `remote.`.
  - Parsing: status lines `1 ...`, `2 ...`, `u ...`, `? new.txt` → `Uncommitted == 4`; `# branch...` and `! ...` lines ignored. `for-each-ref` lines `main\torigin/main\tahead 2`, `feat\t\t`, `old\torigin/old\tgone`, `dev\torigin/dev\tbehind 3` → `Ahead = [main:2]`, `NoUpstream = [feat, old]`. Two stash lines → 2. Empty `remote` output → `HasRemote == false`.
  - Exit 128 with stderr containing `detected dubious ownership` → `GitInspectionException` with `IsDubiousOwnership`.
  - Check: one unsafe repo (1 uncommitted, `main` ahead 2) and two clean → one `Blocker` whose detail names the counts and `main`, and one `Ok` "2 repositories, all pushed". A repo on `E:` (not lost) is not inspected. A repo inside a lost VHDX volume `D:` is inspected. `git.exe` not found with repos present → one `NotChecked` listing their paths. A dubious-ownership repo → `NotChecked` whose action contains `git config --global --add safe.directory`. A test that a remote URL placed in a fake's stdout for any call never appears in any item text.
- [ ] **Step 2:** Run the two classes. Expected: FAIL.
- [ ] **Step 3: Implement** inspector, check and strings.
- [ ] **Step 4:** Run the unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(prepare): git repository check`.

### Task 8: `dev.wsl`, `dev.keys`, `dev.devdrive`

**Files:**
- Create: `src/FormatKit.Prepare/Checks/Developer/WslCheck.cs`, `KeysCheck.cs`, `DevDriveCheck.cs`; strings
- Test: matching test classes

**Interfaces:**
- Consumes: `WslClient.ListDistrosAsync`, `WslClient.IsAvailableAsync`, `IFileSystem`, `WindowsVolume.LostVhdxVolumes`, `context.Profile` (R3).
- Produces: `WslCheck` (`dev.wsl`), `KeysCheck` (`dev.keys`), `DevDriveCheck` (`dev.devdrive`).

- [ ] **Step 1: Write the failing tests** (spec 6.3)
  - WSL distros `Ubuntu`, `docker-desktop`, `docker-desktop-data` → one `Attention` for `Ubuntu` whose action is `wsl --export Ubuntu E:\Ubuntu.tar`. WSL unavailable → no item.
  - Keys: `.ssh\id_ed25519` + `.pub`, `.ssh\deploy` whose first line contains `PRIVATE KEY`, `.ssh\known_hosts` → one `Attention` with count 2 and the backup text. Same with a profile whose `dev` section is enabled with `ssh` → the "profile creates a new key" text. `%APPDATA%\gnupg\private-keys-v1.d\x.key` → `Attention` with `gpg --export-secret-keys`. Nothing → no item. A test that only the first line of a non-`id_` file is read (the fake records reads; a file with a secret on line 2 never has line 2 in any item).
  - Dev Drive: `LostVhdxVolumes` has `D` backed by `C:\DevDrive\dev.vhdx` (seeded 40 GB entry) → `Attention` with `40.0 GB` and `D:`; none → no item.
- [ ] **Step 2:** Run the three classes. Expected: FAIL.
- [ ] **Step 3: Implement.** First-line read: `IFileSystem.OpenRead` + `StreamReader.ReadLine()`, so the rest of the file is never read.
- [ ] **Step 4:** Run the unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(prepare): wsl, key and dev drive checks`.

### Task 9: Apps and FormatKit checks

**Files:**
- Create: `src/FormatKit.Prepare/Checks/Apps/ProgramsExportCheck.cs`, `NetworkDriversCheck.cs`, `ProfileInputsCheck.cs`, `BrowsersCheck.cs`, `src/FormatKit.Prepare/PrepareChecks.cs`; strings
- Test: matching test classes, `PrepareChecksTests.cs`

**Interfaces:**
- Consumes: `context.Inputs`, `context.Profile`, `WindowsVolume.IsLost`, `IFileSystem`.
- Produces: the four checks (`apps.programs-export`, `apps.network-drivers`, `apps.profile-inputs`, `apps.browsers`) and `public static class PrepareChecks { public static IReadOnlyList<IPrepareCheck> All(); }` in spec order: `security.bitlocker`, `security.activation`, `data.user-folders`, `data.other-users`, `data.volumes`, `dev.git-repos`, `dev.wsl`, `dev.keys`, `dev.devdrive`, `apps.programs-export`, `apps.network-drivers`, `apps.profile-inputs`, `apps.browsers`.
- The profile file itself enters `ProfileInputsCheck` as `ExternalInput("", profile.Path, File)` added by the CLI (Task 11); an empty `JsonPath` prints as "the profile file".

- [ ] **Step 1: Write the failing tests** (spec 6.4)
  - Programs export: no profile → `Attention` with `formatkit programs export E:\programs.json`. Input `importFile` `E:\p.json` (exists) → `Ok`; `C:\p.json` → `Blocker`; profile with an enabled programs section and no `importFile` input → `Attention`; profile without a programs section → no item.
  - Network drivers: `E:\net\a\x.inf` → `Ok`; `C:\net` → `Blocker`; `E:\empty` (no `.inf`) → `Blocker` with `drivers backup-network`; `D:\net` where `D` is a lost VHDX volume → `Blocker`; enabled drivers section without the input → `Attention`.
  - Profile inputs: no profile → no item. `direct[1].path` on `C:` → `Blocker` naming `modules.programs.direct[1].path`; missing `dotfiles.path` folder → `Blocker`; profile file on `C:` → `Blocker` "the profile file"; all fine (3 inputs) → one `Ok` "3 profile files are off C:". `importFile` and `networkDriverBackup` inputs never produce items here.
  - Browsers: `%LOCALAPPDATA%\Google\Chrome\User Data`, `%APPDATA%\Mozilla\Firefox\Profiles\abc.default` → two `Attention` items (Chrome, Firefox); Firefox `Profiles` empty → no Firefox item. Paths for Edge `Microsoft\Edge\User Data`, Brave `BraveSoftware\Brave-Browser\User Data`, Vivaldi `Vivaldi\User Data` (all under LocalAppData), Opera `Opera Software\Opera Stable` (AppData).
  - `PrepareChecks.All()` ids equal the list above, in order, with no duplicates.
- [ ] **Step 2:** Run the classes. Expected: FAIL.
- [ ] **Step 3: Implement** checks, `PrepareChecks` and strings.
- [ ] **Step 4:** Run the unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(prepare): apps and profile checks`.

### Task 10: Console and Markdown renderers

**Files:**
- Create: `src/FormatKit.Prepare/Rendering/ConsoleRenderer.cs`, `MarkdownRenderer.cs`; strings
- Test: `tests/FormatKit.Prepare.Tests/Rendering/ConsoleRendererTests.cs`, `MarkdownRendererTests.cs`

**Interfaces:**
- Consumes: `PrepareReport`, `PrepareItem`.
- Produces: `public static class ConsoleRenderer { public static void Write(TextWriter output, PrepareReport report); }`, `public static class MarkdownRenderer { public static string Render(PrepareReport report, string computerName, DateTimeOffset now); }`

- [ ] **Step 1: Write the failing tests** (spec 5), in `en-US` and `pt-BR` (swap `CultureInfo.CurrentUICulture`, restore in `finally`)
  - Console: a report with one item per status prints the status headings in order Blocker, Attention, Not checked, Ok; a group sub-heading before each group's items; the title at 2 spaces, detail and action at 4; empty statuses print no heading; last line `Safe to format.` with no blockers, `2 items block formatting.` with two (pt-BR: `Seguro formatar.` / `2 itens impedem a formatação.`).
  - Markdown: first line `# ` heading with the computer name; the date as `yyyy-MM-dd HH:mm`; `- [ ] ` before Blocker and Attention titles, `- ` before NotChecked and Ok; detail and action as indented continuation lines; same summary line.
  - Both renderers print an item's text unchanged (no escaping that would alter a path), and Markdown escapes only a leading `#` or `-` in a title.
- [ ] **Step 2:** Run the two classes. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4:** Run the unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(prepare): console and markdown renderers`.

### Task 11: `prepare check` command

**Files:**
- Create: `src/FormatKit.Cli/Commands/PrepareCommands.cs`
- Modify: `src/FormatKit.Cli/CliApp.cs` (register the command), CLI `Resources/Strings.resx` + `.pt-BR.resx` (`Cmd_Prepare`, `Cmd_PrepareCheck`, `Opt_PrepareProfile`, `Opt_PrepareOutput`, `Prepare_OutputOnWindows`, `Prepare_OutputFailed`, `Prepare_OutputWritten`)
- Test: `tests/FormatKit.Cli.Tests/PrepareCommandsTests.cs`, `tests/FormatKit.Prepare.Tests/RealMachineTests.cs` (trait `Category=Integration`)

**Interfaces:**
- Consumes: `PrepareChecklist`, `PrepareChecks.All()`, `ConsoleRenderer`, `MarkdownRenderer`, `PrepareContext`, `WindowsVolume`, `IModule.ExternalInputs`, `ProfileLoader`, `context.Env.Elevation.IsElevated`.
- Produces: `internal static class PrepareCommands { public static Command Create(CommandContext context); }` with a test seam `internal static Func<IReadOnlyList<IPrepareCheck>> Checks` defaulting to `PrepareChecks.All` and `internal static Func<PrepareEnvironment> Environment` defaulting to `PrepareEnvironment.Current`.

- [ ] **Step 1: Write the failing tests** (CLI test pattern of `CleanupCommandsTests`, fake checks returning fixed items)
  - No blockers → exit 0, output ends with the safe summary; one blocker → exit 1.
  - `--profile` with an invalid profile → validation errors printed, exit 2, no check ran (fake check records calls).
  - Valid profile → the checks receive `context.Inputs` containing each enabled module's `ExternalInputs` plus `("", <profile path>, File)`; a disabled module contributes nothing.
  - `--output E:\c.md` → file written through `IFileSystem` with the Markdown; an existing file is overwritten; `--output C:\fk\c.md` → `Prepare_OutputOnWindows` warning printed and the file still written; write failure (`InMemoryFileSystem.FailWrites`) → report printed, `Prepare_OutputFailed`, exit 2.
  - `Elevation.RelaunchElevatedAsync` is never called, elevated or not.
  - Integration (real services from `Services.CreateReal(false)`, real checks): the command returns 0 or 1 and prints a summary line. Read-only, safe on the host.
- [ ] **Step 2:** Run `dotnet test tests/FormatKit.Cli.Tests --filter "FullyQualifiedName~PrepareCommands"`. Expected: FAIL.
- [ ] **Step 3: Implement.** Wrap the run in `Output.GuardSystemQueryAsync` like `cleanup check`; build `WindowsVolume` from `Services.Volumes.ListAsync` (on failure: an empty list, so only `%SystemDrive%` counts as lost, and the failure is logged). `--profile` and `--output` are already made absolute by `CliApp.AbsolutizePaths`.
- [ ] **Step 4:** Run the unit suite, then `dotnet test tests/FormatKit.Prepare.Tests --filter "Category=Integration"` on the host. Expected: PASS.
- [ ] **Step 5:** Commit `feat(cli): prepare check command`.

### Task 12: Documentation and VM scenario scripts

**Files:**
- Modify: `README.md`, `docs/testing.md`

- [ ] **Step 1:** README: add `formatkit prepare check [--profile <file>] [--output <file.md>]` to the commands table; a "Before you format" section with an example, the four statuses, what makes an item a Blocker, the exit codes 0/1/2 and the privacy rule; mention `programs export` and `drivers backup-network` as the actions it points to; move "preparation checklist" from Planned to Implemented in "Status"; update the Portuguese summary.
- [ ] **Step 2:** `docs/testing.md`: a "Preparation checklist scenarios (17-22)" section with scenarios 17 to 22 of spec section 8, each with setup commands (`vm.ps1 restore`, `copy`, `run`/`job`), the exact `formatkit prepare check` command and the expected statuses and exit code. Scenario 18 creates the bare remote with `git init --bare C:\remote.git`. Scenario 20 starts with checking `Get-VMSecurity` for a vTPM and says how to record "not covered".
- [ ] **Step 3:** Run `dotnet build` (docs only, sanity). Commit `docs: preparation checklist usage and VM scenarios`.

### Task 13: VM scenarios and findings (needs the user at the VM)

- [ ] **Step 1:** `dotnet publish src/FormatKit.Cli -c Release -o artifacts`; run scenarios 17 to 22 with `tools/vm/vm.ps1` as written in `docs/testing.md`. Scenario 20 elevated part and scenario 21 use `job` (signed-in user, basic `vmconnect` session).
- [ ] **Step 2:** For each defect found: a failing unit test that pins it, the fix, the unit suite green, a commit `fix(prepare): ...`.
- [ ] **Step 3:** Add "Found while running" to the spec with what each scenario showed (including R6 confirmation or correction), and commit `docs: record what the VM preparation scenarios found`.
- [ ] **Step 4:** Ask the user before any push or merge.
