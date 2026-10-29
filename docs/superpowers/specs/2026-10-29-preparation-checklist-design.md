# FormatKit — Preparation Checklist Design

- **Date:** 2026-10-29
- **Status:** Draft, awaiting review
- **Scope:** Sub-project 6 (preparation): a read-only "safe to format?"
  checklist that runs on the old Windows before a reinstall
- **Builds on:** `2026-10-27-core-and-drivers-design.md` (profile loader,
  system access, `drivers backup-network`), `2026-10-27-cleanup-design.md`
  (`cleanup check` as the model for a read-only diagnosis),
  `2026-10-28-programs-design.md` (`programs export`, `importFile`, `direct`)
  and `2026-10-28-dev-environment-design.md` (`dotfiles.path`, `dev.ssh`,
  Dev Drive)

## 1. Purpose

Before formatting, answer one question: **is it safe to format this PC?**
FormatKit inspects the old Windows and lists what must still be saved or
exported, so no personal data is lost and the profile can rebuild the machine
afterwards. Each item says what was found and what to do about it.

FormatKit still does not migrate the old system (core spec, section 1). The
checklist only points; the user does the backup.

### Success criteria

- A user who follows every `Blocker` and `Attention` action loses nothing
  FormatKit can see, and their profile's input files survive the format.
- A non-developer can read the output: every item has a plain action.
- The command never changes the system and never asks for elevation, so it is
  safe on the host and on any friend's PC.

## 2. Scope

**In 6:**

- A new project `FormatKit.Prepare` with the checks in section 6.
- Command `formatkit prepare check [--profile <file>] [--output <file.md>]`.
- A default method `IModule.ExternalInputs` implemented by Programs, Drivers
  and Dev, so the checklist can see the files a profile needs.
- Two new system access services, `IVolumes` and `ILicensing`, and two
  read-only `IFileSystem` methods.

**Out of scope:** copying or exporting anything (the user does it; the
existing `programs export` and `drivers backup-network` are referenced, not
wrapped); Wi-Fi passwords; game saves; app licenses such as Office or Adobe
(no reliable detection); OneDrive's real sync state; printers; the post-run
report (it already exists in the core); installation media (sub-project 5).

## 3. Decisions

| # | Decision | Reason |
|---|---|---|
| D1 | Check and guide only; nothing is copied or exported. | Keeps the command read-only and safe on any PC; backups need space, time and choices only the user can make. |
| D2 | New project `FormatKit.Prepare` with one class per check (approach A). Not an `IModule`: it never runs inside a profile. | One coherent checklist that is easy to order, group and test. Per-module checks (approach B) would spread it over five projects; a JSON rule catalog (approach C) fits registry values, not repository scans or folder sizes. |
| D3 | Modules only declare the paths their section needs (`IModule.ExternalInputs`). | The checklist reuses each module's own relative-path rules instead of duplicating them. |
| D4 | Never elevates. What needs administrator rights becomes `NotChecked` with the command to check it by hand; an already elevated shell checks it. | Same rule as `cleanup check`: a read-only command must not ask for UAC. |
| D5 | `Blocker` only for loss FormatKit can prove; what only the user can confirm is `Attention`. | Otherwise the command could never report "safe" (it cannot know a recovery key was saved). |
| D6 | Exit code 0 when there is no `Blocker`, 1 when there is, 2 for invalid arguments, an invalid profile or a failed `--output` write. | Scriptable, like `cleanup check`. |
| D7 | Output never contains secrets: no Git remote URLs, product keys, recovery keys or Wi-Fi passwords. Only names, paths, sizes and counts. | Remote URLs can embed tokens; the Markdown file travels on USB drives. |
| D8 | A path is lost in the format when it is on `%SystemDrive%` or on a volume whose backing VHDX is on `%SystemDrive%`. UNC paths are never lost. | A Dev Drive at `C:\DevDrive\dev.vhdx` mounted as `D:` disappears with `C:`. |

## 4. Architecture

New project `FormatKit.Prepare`, referencing only `FormatKit.Core`. Tests in
`tests/FormatKit.Prepare.Tests`.

| Unit | Responsibility |
|---|---|
| `PrepareStatus` | `Blocker`, `Attention`, `NotChecked`, `Ok` (in that sort order). |
| `PrepareGroup` | `Security`, `Data`, `Developer`, `Apps` (in that sort order). |
| `PrepareItem` | Record: `CheckId`, `Group`, `Status`, `Title`, `Detail?`, `Action?`. Text is already localized. |
| `IPrepareCheck` | `Id`, `Group`, `Task<IReadOnlyList<PrepareItem>> RunAsync(PrepareContext, CancellationToken)`. One class per check id in section 6. |
| `PrepareContext` | `ServiceSet`, the loaded profile or null, the external inputs, `IsElevated`, the user profile folder, and a `WindowsVolume`. |
| `WindowsVolume` | `bool IsLost(string path)` per D8. Built once from `%SystemDrive%` and `IVolumes`. |
| `PrepareChecklist` | Runs the checks in order. A check that throws becomes one `NotChecked` item for that check ("see the log"), and the exception is logged; the other checks still run (as in `CleanupDiagnosis`). Sorts by status, then group, then check id. |
| `PrepareReport` | The items and `HasBlockers`. |
| `ConsoleRenderer`, `MarkdownRenderer` | Pure functions from `PrepareReport` to text. |
| `GitRepoInspector` | Finds repositories and reads their state through `IProcessRunner` (`git -C <repo> ...`). Never reads remote URLs, only whether a remote exists. |
| CLI `Commands/PrepareCommands` | `prepare check`: arguments, profile load, rendering, `--output`, exit code. |

### 4.1 Core changes

- `IModule.ExternalInputs(JsonElement section, string? baseDirectory)`: a
  default method returning an empty list. Each item is
  `ExternalInput(string JsonPath, string ResolvedPath, ExternalInputKind Kind)`
  with `Kind` `File` or `Folder`. `JsonPath` uses the profile error format,
  for example `modules.programs.importFile` or `modules.programs.direct[2].path`.
  Implemented by:
  - Programs: `importFile` (File) and each `direct[i].path` (File; `url` items
    are not inputs).
  - Drivers: `networkDriverBackup` (Folder).
  - Dev: `dotfiles.path` (Folder; a `repo` is not an input).
  Relative paths resolve exactly as the module's steps resolve them (against
  the profile file's folder).
- `IFileSystem` gains, both read-only:
  - `IEnumerable<string> EnumerateDirectories(string dir)`: one level only.
  - `IEnumerable<FileEntry> EnumerateFileEntries(string dir, bool recursive)`
    with `FileEntry(string Path, long Length, FileAttributes Attributes)`.
  Both skip reparse points (links and junctions), so scans neither loop nor
  count twice. A folder that cannot be read is skipped and reported to the
  caller through a callback, not thrown.
- New services, added as the last parameters of `ServiceSet`, wired in
  `src/FormatKit.Cli/Services.cs` and given fakes in
  `tests/FormatKit.Core.Tests/Fakes/TestServices.cs`:
  - `IVolumes.ListAsync(ct)` returns `VolumeInfo(char Letter, int? DiskNumber,
    BitLockerState BitLocker, string? BackingVhdx, bool? HasRecoveryPassword)`
    for local fixed volumes with a letter. `DiskNumber` is null when it cannot
    be read without elevation; `HasRecoveryPassword` is null unless elevated.
    `BitLockerState` is `Off`, `On`, `Encrypting`, `Decrypting`, `Suspended`,
    `Locked` or `Unknown`, from the Shell property
    `System.Volume.BitLockerProtection` (readable without elevation). The
    protectors come from `Win32_EncryptableVolume` (elevated only). The
    implementation uses PowerShell and CIM like `PowerShellVirtualDisks`. The
    exact property values and which CIM classes need elevation are verified
    during implementation and pinned by tests.
  - `ILicensing.GetWindowsActivationAsync(ct)` returns
    `WindowsActivation(bool Activated, string? Channel)` from CIM
    `SoftwareLicensingProduct` (Windows application id, a partial product key
    present). The product key itself is never read.
- Already available and reused: `IRegistry` (`User Shell Folders`,
  `ProfileList`), `WslClient.ListDistrosAsync`, `IElevation.IsElevated`,
  `IToolLocator` (to find `git.exe`), `IProcessRunner` with the query timeout.

### 4.2 Flow

1. The CLI parses arguments. With `--profile`, it loads the profile through
   `ProfileLoader`. An invalid profile prints the validation errors, as `run`
   does, and exits with 2 without running any check.
2. It collects `ExternalInputs` from every enabled module section and adds the
   profile file itself as an input.
3. It builds the `PrepareContext` and runs `PrepareChecklist`.
4. It prints the report, writes `--output` if asked, and exits per D6.

## 5. Command and output

```
formatkit prepare check [--profile <file>] [--output <file.md>]
```

- **Console:** items grouped by status (Blocker, Attention, Not checked, Ok);
  inside a status, by group (Security and licenses, Personal data, Developer,
  Apps and FormatKit), then by check id. Each item prints its title, then its
  detail and action indented. Successful items of the same check are collapsed
  into one line where section 6 says so. The last line is a summary: "Safe to
  format." or "N items block formatting.".
- **`--output <file.md>`:** the same content as Markdown in the UI language,
  with a heading per status, `- [ ]` before every `Blocker` and `Attention`
  item and a plain bullet for the others, plus the date and computer name at
  the top. When the output path is lost in the format (D8), the console prints
  a warning that the file will be erased; the file is written anyway. An
  existing file is overwritten.
- **Exit codes:** 0 no `Blocker`; 1 at least one `Blocker`; 2 invalid
  arguments, invalid profile or failed `--output` write (the report is still
  printed first); 130 Ctrl+C.
- Output language follows the Windows UI language (English and Brazilian
  Portuguese), like the other commands.

## 6. Checks

"Lost" means lost in the format per D8. "With profile" describes what changes
when `--profile` is given.

### 6.1 Security and licenses

| Id | Reads | Result |
|---|---|---|
| `security.bitlocker` | `IVolumes`: BitLocker state of each fixed volume; recovery password protector when elevated. | Windows volume encrypted: `Ok`, noting that reinstalling turns device encryption on again. Another volume not `Off`: `Attention` "save the recovery key of D: (aka.ms/myrecoverykey, or `manage-bde -protectors -get D:`); without it, D: does not open after the reinstall". When elevated and that volume has no recovery password protector, the action says to add one (`manage-bde -protectors -add D: -RecoveryPassword`). Not elevated: one extra `NotChecked` item per encrypted non-Windows volume with the `manage-bde -protectors -get` command. State `Unknown`: `NotChecked`. |
| `security.activation` | `ILicensing`. | Activated, channel `OEM:DM` (key in firmware): `Ok`, it reactivates by itself. Activated `Retail`: `Attention` "link the license to your Microsoft account in Settings > System > Activation, or keep your product key". Activated `Volume:*`: `Attention` "licensed by an organization; ask its IT". Any other channel while activated: `Attention` with the Retail text. Not activated: `Attention` "Windows is not activated; you will need a product key". |

### 6.2 Personal data

| Id | Reads | Result |
|---|---|---|
| `data.user-folders` | Desktop, Documents, Pictures, Videos, Music and Downloads from `HKCU\...\Explorer\User Shell Folders` (expanded, so redirected folders are found). Total size of each, skipping files with `RecallOnDataAccess` or `Offline` (cloud-only placeholders). | Folder under the user's OneDrive folder (`OneDrive` environment value): `Ok` "synced by OneDrive; make sure it is up to date". Folder not lost: `Ok`. Folder lost with files: `Attention` with the size, "copy it to an external drive". Empty: `Ok`. Folders that could not be read are counted in the detail. |
| `data.other-users` | `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList`: local profiles other than the current user, skipping well-known system SIDs (`S-1-5-18`, `-19`, `-20`) and profiles whose folder is missing. | Each profile: `Attention` "the account X also has files that will be lost". Its size is shown only when readable. |
| `data.volumes` | `IVolumes`: other fixed volumes and their disk number. | Another volume on the same disk as Windows: `Attention` "in Windows Setup, delete only the Windows partitions; do not delete D:". On another disk: `Ok` (informational). Disk numbers unreadable: one `NotChecked` item. A volume backed by a lost VHDX is handled by `dev.devdrive`, not here. |

### 6.3 Developer

| Id | Reads | Result |
|---|---|---|
| `dev.git-repos` | Folders containing `.git` (folder or file) under the user profile, up to 5 levels deep, skipping `AppData`, `node_modules`, hidden folders and reparse points; and the root of every volume backed by a lost VHDX, same depth. The scan does not descend into a repository it found (a submodule shows up in its parent's status). For each: `git status --porcelain=v2 --branch`, `git for-each-ref --format=... refs/heads` (no upstream, or ahead), `git stash list`, `git remote` (names only). | Per repository with any of: uncommitted changes, commits not pushed, local branch without upstream, stash entries, no remote at all: `Blocker` with what is missing (counts and branch names) and the action "commit and push, or copy the folder". All clean: one `Ok` line "N repositories, all pushed". Repositories found but `git.exe` missing: `NotChecked` listing their paths. A repository whose git call fails or times out: `NotChecked` for that repository. Repositories that are not lost are skipped. |
| `dev.wsl` | `WslClient.ListDistrosAsync`, skipping `docker-desktop` and `docker-desktop-data`. | Each distro: `Attention` "export it with `wsl --export <distro> E:\<distro>.tar`". No WSL: no item. |
| `dev.keys` | `%USERPROFILE%\.ssh`: private keys (an `id_*` file without `.pub`, or a file whose first line contains `PRIVATE KEY`). `%APPDATA%\gnupg\private-keys-v1.d` with files. Only names and counts are read; key contents beyond the first line are never read. | SSH keys present: `Attention` "back them up, or plan to register a new key". With a profile that has `dev.ssh`: "the profile creates a new key; remember to remove the old one from GitHub and add the new one". GPG secret keys: `Attention` "export them with `gpg --export-secret-keys`". Nothing found: no item. |
| `dev.devdrive` | `IVolumes`: volumes whose backing VHDX is on the Windows volume. | Each: `Attention` with the VHDX size, "this virtual disk is on C: and will be erased; copy what you need". Repositories inside are reported by `dev.git-repos`. |

### 6.4 Apps and FormatKit

| Id | Without profile | With profile |
|---|---|---|
| `apps.programs-export` | `Attention` "run `formatkit programs export E:\programs.json` and keep the file off C:". | `importFile` present and not lost: `Ok`. Lost: `Blocker` "move it off C: and update the profile". Section without `importFile`: `Attention` as without profile. Programs section disabled or absent: no item. |
| `apps.network-drivers` | `Attention` "run `formatkit drivers backup-network --output E:\netdrivers`". | `networkDriverBackup` not lost and holding at least one `.inf` (recursive): `Ok`. Lost: `Blocker`. Missing or without `.inf`: `Blocker` "run `drivers backup-network` into it". Not set: `Attention` as without profile. Drivers section disabled or absent: no item. |
| `apps.profile-inputs` | No item. | Every other external input (`direct[].path`, `dotfiles.path`) and the profile file itself: lost → `Blocker` naming the JSON path (or "the profile file"); missing → `Blocker`; otherwise one `Ok` line "N profile files are off C:". `importFile` and `networkDriverBackup` are left to the two checks above, so no path is reported twice. |
| `apps.browsers` | Browsers with a user data folder: Edge, Chrome, Brave, Vivaldi (`%LOCALAPPDATA%\...\User Data`), Opera (`%APPDATA%\Opera Software\Opera Stable`), Firefox (`%APPDATA%\Mozilla\Firefox\Profiles` with a profile). Each: `Attention` "turn on sync or export bookmarks and passwords". | Same. |

## 7. Error handling

- A check that throws becomes one `NotChecked` item ("could not be checked;
  see the log") and never stops the others.
- What needs elevation is `NotChecked` with the manual command, not an error.
- Every external call uses the query timeout. A repository that times out is
  `NotChecked`; the scan goes on.
- Folders the scan cannot read are skipped and counted in the item's detail
  ("N folders could not be read").
- `--output` failure: the report has already been printed; the error is shown
  and the exit code is 2.
- Ctrl+C exits with 130.
- The log has every external command, its exit code and output. No command
  that prints a remote URL is ever run (`git remote` lists names only;
  `git remote -v` and `git config remote.*.url` are never used), so the log
  holds none either.

## 8. Testing

- **Unit (`tests/FormatKit.Prepare.Tests`, fakes only):**
  - Every row of section 6 has at least one test, for example "`importFile` on
    `C:\` is a Blocker" and "a `networkDriverBackup` on `D:` backed by
    `C:\DevDrive\dev.vhdx` is a Blocker".
  - `WindowsVolume`: same letter in any case, UNC, VHDX on `C:`, other drives.
  - `GitRepoInspector`: parsing `porcelain=v2`, `for-each-ref` and `stash`
    output; depth limit and skipped folders; repository without a remote;
    `git.exe` missing; timeout.
  - `PrepareChecklist`: a throwing check becomes `NotChecked`; sort order.
  - Renderers: console and Markdown in en and pt-BR; a test that a remote URL
    given to the fakes never appears in either output.
- **Module tests:** `ExternalInputs` of Programs, Drivers and Dev with relative
  and absolute paths, and with the section disabled.
- **CLI:** exit codes 0, 1 and 2; `--output` written and overwritten; output on
  the Windows volume warns; invalid profile exits 2 without running checks.
- **Integration (trait `Integration`, read-only):** `prepare check` against the
  real machine completes and returns 0 or 1. The command only reads, so it is
  safe on the host.
- **VM scenarios** (`docs/testing.md`, numbered after the existing ones):
  - 17: `clean` VM, no profile: only `Attention` and `Ok`, exit 0.
  - 18: in `Documents`, one repository with an unpushed commit and one with an
    uncommitted change: two `Blocker` items, exit 1. After committing and
    pushing to a local bare repository: exit 0.
  - 19: `--profile` with `importFile` and `networkDriverBackup` on `C:`:
    `Blocker`; moved to `F:`: `Ok`.
  - 20: BitLocker on the data disk `F:` when the VM has a vTPM: `Attention`,
    protectors `NotChecked` when not elevated, recovery password detected in
    an elevated shell. Without a vTPM the scenario is recorded as not covered.
  - 21: WSL with one distro (`nested` checkpoint): `Attention` with the export
    command.
  - 22: `--output F:\checklist.md` has the check boxes; `--output C:\fk\c.md`
    prints the erase warning.

## 9. Documentation

- README: the command in the table, a "Before you format" section with an
  example and the exit codes, "Status" and the Portuguese summary updated.
- This spec gets a "Found while running" section after the VM scenarios.

## 10. Review focus

The input classes most likely to bite users that unit tests can miss:

1. Large user profiles: the folder size sum and the repository scan must stay
   fast enough (a few minutes at most) and survive unreadable folders.
2. OneDrive Known Folder Move with Files On-Demand: placeholders must not
   count as local data, and redirected paths must be read from the registry.
3. Non-English Windows: folder names and `manage-bde` output differ; checks
   must use registry values, property ids and exit codes, not display text.
4. Repositories in odd states: detached HEAD, worktrees (`.git` is a file),
   bare repositories, `safe.directory` refusals for folders owned by another
   user.
5. Volumes without a letter, removable drives reported as fixed, and BitLocker
   states during encryption or suspension.
