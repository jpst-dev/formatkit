# Dev Environment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the `dev` module (WSL, Git, SSH, dotfiles, shell, Dev Drive), the cleanup `developer` category, the `development.oh-my-posh` catalog entry and the `dev wsl-user` / `dev ssh-key` commands.

**Architecture:** New project `FormatKit.Dev` (references only `FormatKit.Core`) with one `IStep` per profile subsection, each reporting per-item outcomes. New system adapters live in `FormatKit.Core/SystemAccess` and join `ServiceSet`. Windows developer settings are data in the existing cleanup catalog.

**Tech Stack:** .NET 10 `net10.0-windows`, TreatWarningsAsErrors, System.CommandLine 2.0.14, Serilog, xunit.v3.

**Spec:** `docs/superpowers/specs/2026-10-28-dev-environment-design.md`

## Global Constraints

- Code, identifiers and repo text in English; user-facing strings in `Resources/Strings.resx` (en) and `Strings.pt-BR.resx`, read through each project's `Localization/Strings`.
- Every external value goes on a command line through `ProcessSpec.Arguments`, never a shell string. Values inside a Linux command go as separate `wsl.exe --exec` arguments; the only `sh -c` scripts are the constant ones defined in Task 3.
- FormatKit never reads, logs or stores a password or passphrase: `passwd`, `ssh-keygen`, `ssh-add` run through `IInteractiveConsole.RunAttachedAsync` (no capture).
- Validation errors fail the profile with exit 2 before anything runs; errors carry the JSON path (`modules.dev.wsl.user`).
- One failed item never stops the others (spec D10).
- Tests and reviews never write to the host's registry, services, power settings or disks; system-changing checks happen only in the VM (`.vm/test-vm.json`, read masked).
- Unit suite: `dotnet test -- --filter-not-trait "Category=Integration"`; it must stay green after every task.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. WSL 2 cannot start (virtualization off): `wsl` output contains `HCS_E_HYPERV_NOT_INSTALLED` or `0x80370102` → item failed with "enable virtualization in the BIOS/UEFI", never a hang. Test in Task 8.
2. `wsl.exe` writes UTF-16 unless `WSL_UTF8=1`: every `WslClient` call sets it, and the distro list strips `\0` and BOM defensively. Test in Task 3.
3. A resumed run with nobody at the machine: `CanPrompt == false` → password items end pending and `RunAttachedAsync` is never called. Tests in Tasks 8 and 9.
4. Dotfiles differing only in CRLF/LF are different files (byte compare): backed up and replaced; the README says so. Test in Task 10.
5. Dev Drive creation failing after the VHDX exists (letter taken meanwhile, format error): VHDX detached and deleted, item failed, retry starts clean. Test in Task 12.

## Plan rulings (decisions the spec leaves open)

- **R1** Every dev step's `CheckAsync` returns `false`; idempotence is reported per item as "already in order". The engine never re-runs a finished step on resume, so the cost is one extra listing.
- **R2** `cleanup.level` becomes optional when `include` has at least one id, so a profile can apply only developer items.
- **R3** `shell.windows.font` is limited to `Meslo`, `CascadiaCode`, `FiraCode`, `JetBrainsMono`, `Hack` (each maps to a known family name for the Terminal fragment). Tighter than spec section 5.
- **R4** The sudo item's "not applicable" uses a new optional `ifFileExists` on registry actions; a missing file throws `TargetNotPresentException` (existing "not applicable" path).
- **R5** Fresh `PATH` for tool lookup comes from the machine and user `Path` values, not the stale process `PATH`.

---

### Task 1: VM probes (controller, no subagent)

Resolve the spec's open facts before code depends on them. Restore the clean checkpoint with the scratchpad `vm.ps1`; enable nested virtualization on the VM first (`Set-VMProcessor -VMName FormatKit-Test -ExposeVirtualizationExtensions $true` while it is off — a VM setting, not a host system setting).

- [ ] **Step 1:** `wsl --install --no-distribution` on a clean VM: record its exit code, output and whether `VirtualMachinePlatform` is `Enabled` or pending until reboot; after reboot `wsl --status` exit code.
- [ ] **Step 2:** `wsl --install Ubuntu --no-launch`, then `wsl -d Ubuntu -u root --exec id -u joao` (exit 1), `useradd -m -G sudo -s /bin/bash joao`, `passwd -S joao` (expect `L`), `passwd joao` from an elevated console with stdin attached (password prompt works), `/etc/wsl.conf` default user after `wsl --terminate`.
- [ ] **Step 3:** Dev Drive on a VHDX: run the diskpart script and `Format-Volume -DevDrive` from spec 6.6; `fsutil devdrv query D:` shows trusted; reboot; record whether `D:` is back without a task. If not, confirm `Mount-DiskImage` from a SYSTEM startup task brings it back.
- [ ] **Step 4:** Windows Terminal fragment at `%LOCALAPPDATA%\Microsoft\Windows Terminal\Fragments\FormatKit\formatkit.json` with `{"profiles":[{"updates":"{61c54bbd-c2c6-5271-96e7-009a87ff44bf}","font":{"face":"MesloLGM Nerd Font"}}]}`: record whether Windows PowerShell's font changes.
- [ ] **Step 5:** `winget show JanDeDobbeleer.OhMyPosh` (id, scope), install it, `oh-my-posh font install meslo` (exit code on second run), the working `init` line for pwsh and powershell 5.1 with a built-in theme name (`paradox`).
- [ ] **Step 6:** Each spec section 7 registry value: set it and confirm the effect (Settings > For developers shows Developer Mode on; `sudo` works inline on 24H2; `Get-ExecutionPolicy -Scope LocalMachine` is `RemoteSigned`; Explorer shows extensions/hidden after restart).
- [ ] **Step 7:** Append a section `## 12. Verified in the VM` to the spec with each finding (one line each), and adjust spec lines a finding contradicts. Commit: `docs: record dev environment VM findings`.

Later tasks use these findings: Task 8 (reboot signal), Task 11 (fragment and init line), Task 12 (remount task needed or not), Task 5 (items kept).

### Task 2: Core plumbing

**Files:**
- Modify: `src/FormatKit.Core/SystemAccess/IProcessRunner.cs`, `ProcessRunner.cs`, `IFileSystem.cs`, `FileSystem.cs`, `src/FormatKit.Core/Steps/ServiceSet.cs`, `src/FormatKit.Cli/Services.cs`
- Create: `src/FormatKit.Core/SystemAccess/IInteractiveConsole.cs`, `WindowsInteractiveConsole.cs`, `IUserEnvironment.cs`, `RegistryUserEnvironment.cs`, `IToolLocator.cs`, `ToolLocator.cs`
- Test fakes: `tests/FormatKit.Core.Tests/Fakes/FakeInteractiveConsole.cs`, `FakeUserEnvironment.cs`, `FakeToolLocator.cs`; modify `InMemoryFileSystem.cs`, `FakeProcessRunner.cs` (if it records specs), `TestServices.cs`
- Test: `tests/FormatKit.Core.Tests/SystemAccess/ToolLocatorTests.cs`, `RegistryUserEnvironmentTests.cs`, `InMemoryFileSystemTests.cs` (new cases)

**Interfaces:**
- Produces:
  - `ProcessSpec(string FileName, IReadOnlyList<string> Arguments, TimeSpan Timeout, IReadOnlyDictionary<string, string>? Environment = null)`; `ProcessRunner` adds each pair to `startInfo.Environment`.
  - `IFileSystem`: `byte[] ReadAllBytes(string path)`, `void Copy(string source, string destination)` (overwrites), `void Move(string source, string destination)` (fails if destination exists).
  - `interface IInteractiveConsole { bool CanPrompt { get; } Task<int> RunAttachedAsync(ProcessSpec spec, CancellationToken ct); void WriteLine(string text); }`. Real: `CanPrompt = !Console.IsInputRedirected && session.HasInteractiveUser`; `RunAttachedAsync` starts with `UseShellExecute = false` and no redirection, logs only the file name, kills the process on `spec.Timeout`, returns the exit code.
  - `interface IUserEnvironment { string? Get(string name); void Set(string name, string value); }`. Real `RegistryUserEnvironment(IRegistry registry)` reads/writes `HKCU\Environment` (REG_SZ) and broadcasts `WM_SETTINGCHANGE` with `"Environment"` via `SendMessageTimeout(HWND_BROADCAST, …, SMTO_ABORTIFHUNG, 5000)`.
  - `interface IToolLocator { string? Find(string exeName, IReadOnlyList<string> knownPaths); }`. Real `ToolLocator(IFileSystem files, Func<string, string?> machinePath, Func<string, string?> userPath)`: checks each directory of the machine then user `Path` (R5), then each known path after `Environment.ExpandEnvironmentVariables`; first existing file wins.
  - `ServiceSet` appends `WslClient Wsl, IInteractiveConsole Console, IUserEnvironment UserEnvironment, IToolLocator Tools, IVirtualDisks VirtualDisks` (the last two types come from Tasks 3–4; add `Wsl` and `VirtualDisks` in those tasks, the other three here).
  - `TestServices.Create(...)` gains optional `FakeInteractiveConsole? console`, `FakeUserEnvironment? userEnvironment`, `FakeToolLocator? tools` (and later `FakeVirtualDisks? virtualDisks`).
  - `FakeInteractiveConsole { bool CanPrompt { get; set; } = true; List<ProcessSpec> Attached; Func<ProcessSpec,int> ExitCode; List<string> Lines; }`.

- [ ] **Step 1:** Tests: `ToolLocator_prefers_fresh_path_over_known_paths`, `ToolLocator_falls_back_to_known_path`, `ToolLocator_returns_null_when_missing` (with `InMemoryFileSystem`); `RegistryUserEnvironment_writes_hkcu_environment` (with `FakeRegistry`, asserts path `HKCU\Environment`, kind `String`); `InMemoryFileSystem_copy_move_readallbytes`.
- [ ] **Step 2:** Run them; expect compile failures.
- [ ] **Step 3:** Implement the interfaces above; wire real adapters in `Services.CreateReal`.
- [ ] **Step 4:** Run the unit suite; expect all green.
- [ ] **Step 5:** Commit `feat(core): add console, user environment and tool lookup adapters`.

### Task 3: `WslClient`

**Files:** Create `src/FormatKit.Core/SystemAccess/WslClient.cs`; test `tests/FormatKit.Core.Tests/SystemAccess/WslClientTests.cs`; add `Wsl` to `ServiceSet`, `Services.cs`, `TestServices`.

**Interfaces:**
- Produces `public sealed class WslClient(IProcessRunner processes)`:
  - `const string Exe = "wsl.exe"`; every spec it builds has `Environment = { ["WSL_UTF8"] = "1" }`.
  - `Task<bool> IsAvailableAsync(CancellationToken ct)` — `wsl --status`, exit 0; `FileNotFoundException` → false.
  - `Task<ProcessResult> InstallPlatformAsync(CancellationToken ct)` — `wsl --install --no-distribution`, `Timeouts.Install`.
  - `Task<IReadOnlyList<string>> ListDistrosAsync(CancellationToken ct)` — `wsl --list --quiet`; split lines, strip `\0`, `\uFEFF`, whitespace; drop empties; exit ≠ 0 → empty list.
  - `Task<ProcessResult> InstallDistroAsync(string distro, CancellationToken ct)` — `wsl --install <distro> --no-launch`, `Timeouts.Install`.
  - `Task<ProcessResult> ExecAsync(string distro, string user, IReadOnlyList<string> command, CancellationToken ct, TimeSpan? timeout = null)` — `wsl -d <distro> -u <user> --exec <command...>`, default `Timeouts.Query`.
  - `Task<ProcessResult> WriteFileAsync(string distro, string user, string path, string content, CancellationToken ct)` — `ExecAsync(distro, user, ["sh", "-c", WriteScript, "sh", path, content])` with `internal const string WriteScript = "printf '%s' \"$2\" > \"$1\""`.
  - `Task TerminateAsync(string distro, CancellationToken ct)` — `wsl --terminate <distro>`.
  - `static ProcessSpec ExecSpec(string distro, string user, IReadOnlyList<string> command, TimeSpan timeout)` — the spec `ExecAsync` runs, reused for console-attached commands.
  - `static bool IsVirtualizationError(ProcessResult r)` — stdout or stderr contains `HCS_E_HYPERV_NOT_INSTALLED` or `0x80370102`.

- [ ] **Step 1:** Tests: `Every_call_sets_WSL_UTF8`, `ListDistros_strips_nulls_and_bom` (stdout `"\uFEFFUbuntu\0\r\nDebian\r\n\r\n"` → `["Ubuntu","Debian"]`), `Exec_passes_command_as_separate_arguments` (asserts args `["-d","Ubuntu","-u","root","--exec","id","-u","joao"]`), `WriteFile_uses_constant_script` (args end with `"sh","-c",WriteScript,"sh","/etc/wsl.conf","[user]\ndefault=joao\n"`), `IsVirtualizationError_detects_both_tokens`, `IsAvailable_false_when_wsl_missing`.
- [ ] **Step 2:** Run; expect failures.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Suite green.
- [ ] **Step 5:** Commit `feat(core): add WSL client`.

### Task 4: Virtual disks

**Files:** Create `src/FormatKit.Core/SystemAccess/IVirtualDisks.cs`, `PowerShellVirtualDisks.cs`; fake `tests/FormatKit.Core.Tests/Fakes/FakeVirtualDisks.cs`; test `tests/FormatKit.Core.Tests/SystemAccess/PowerShellVirtualDisksTests.cs`; add `VirtualDisks` to `ServiceSet`, `Services.cs`, `TestServices`.

**Interfaces:**
- Produces:
  - `public sealed record VirtualDiskState(bool Attached, char? Letter, string? Label);`
  - `public sealed class VirtualDiskException(string message, Exception? inner = null) : Exception(message, inner);`
  - `public interface IVirtualDisks` with `Task<VirtualDiskState?> GetAsync(string vhdxPath, CancellationToken ct)` (null when the file does not exist), `Task AttachAsync(string vhdxPath, CancellationToken ct)`, `Task CreateDevDriveAsync(string vhdxPath, int sizeGB, char letter, string label, CancellationToken ct)`, `Task DetachAndDeleteAsync(string vhdxPath, CancellationToken ct)`, `bool IsLetterInUse(char letter)`, `long FreeBytes(string hostRoot)`, `Task RegisterRemountTaskAsync(string vhdxPath, CancellationToken ct)`.
  - `PowerShellVirtualDisks(IProcessRunner processes, IFileSystem files)`:
    - `GetAsync`: `powershell -NoProfile -NonInteractive -Command` with a constant script taking the path as `$args[0]` (`Get-DiskImage -ImagePath $args[0]`, partition → volume, `ConvertTo-Json`), parsed into `VirtualDiskState`.
    - `CreateDevDriveAsync`: writes a diskpart script to `%ProgramData%\FormatKit\devdrive.txt` (`create vdisk file="<path>" maximum=<sizeGB*1024> type=expandable`, `select vdisk file="<path>"`, `attach vdisk`, `convert gpt`, `create partition primary`, `assign letter=<letter>`), runs `diskpart /s <file>`, then `Format-Volume -DriveLetter <letter> -DevDrive -NewFileSystemLabel '<label>' -Confirm:$false`; any non-zero exit → `VirtualDiskException` after calling `DetachAndDeleteAsync` when the file exists; the script file is deleted in `finally`.
    - `RegisterRemountTaskAsync`: task `FormatKit-DevDrive`, trigger at startup, principal SYSTEM, action `powershell -NoProfile -Command Mount-DiskImage -ImagePath '<path>'`; only called when Task 1 found it necessary.
  - `FakeVirtualDisks` keeps a dictionary of states, records calls, and can be told to throw from `CreateDevDriveAsync`.

- [ ] **Step 1:** Tests (with `FakeProcessRunner`, no real disks): `Create_writes_expected_diskpart_script`, `Create_failure_detaches_and_deletes`, `Script_file_deleted_after_run`, `Get_parses_attached_state`, `Get_returns_null_for_missing_file`. Paths with spaces are quoted inside the diskpart script; the label never contains a quote (validated in Task 7).
- [ ] **Step 2:** Run; expect failures.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Suite green.
- [ ] **Step 5:** Commit `feat(core): add virtual disk adapter for Dev Drive`.

### Task 5: Cleanup `developer` category

**Files:** Modify `src/FormatKit.Cleanup/Catalog/CleanupCatalog.cs` (enum), `CleanupModule.cs` (`CategoryOrder`, R2), `ItemSelector.cs`, `Catalog/CleanupActions.cs` (`RegistryAction` gains `string? IfFileExists`), `Actions/RegistryHandler.cs`, `Catalog/CatalogValidator.cs` (if it lists categories), `Catalog/Data/cleanup.json`, both `Resources/Strings*.resx` (`Step_Title_Developer`: "Cleanup: developer settings" / "Limpeza: ajustes para desenvolvedores"); tests in `tests/FormatKit.Cleanup.Tests`.

**Interfaces:**
- Produces: `CleanupCategory.Developer` (JSON `"developer"`), step id `cleanup.developer`, run after Performance.

- [ ] **Step 1:** Tests: `Developer_items_never_selected_by_any_level` (Safe, Balanced, Aggressive → none of the 6), `Developer_item_selected_by_include`, `Level_optional_when_include_present` (no `level`, `include: ["developer.long-paths"]` → valid, one step `cleanup.developer`), `Level_still_required_without_include`, `Registry_action_with_missing_ifFileExists_is_not_applicable`, catalog test: the six ids exist with en and pt-BR text.
- [ ] **Step 2:** Run; expect failures.
- [ ] **Step 3:** Implement. Catalog entries: the six items of spec section 7 with the exact paths/values there, `level: "safe"`, `contested: false`, `laptopSensitive: false`; `scope` `machine` except `show-extensions` and `show-hidden` (`user`); `requiresReboot` true for `long-paths` only; `developer.sudo` action has `"ifFileExists": "%SystemRoot%\\System32\\sudo.exe"`. Drop any item Task 1 could not confirm and name it in the commit message. Texts follow the existing what / breaks / gain style.
- [ ] **Step 4:** Suite green.
- [ ] **Step 5:** Commit `feat(cleanup): add developer settings category`.

### Task 6: Programs catalog entry

**Files:** Modify `src/FormatKit.Programs/Catalog/Data/programs.json`, `tests/FormatKit.Programs.Tests/Catalog/EmbeddedCatalogTests.cs`.

- [ ] **Step 1:** Add `development.oh-my-posh` (`JanDeDobbeleer.OhMyPosh`, source `winget`, scope and context as Task 1 Step 5 found; en "Oh My Posh" / "Prompt theme engine for PowerShell and other shells.", pt-BR "Oh My Posh" / "Temas de prompt para o PowerShell e outros terminais."). Update the PerUser or MachineScope list in the test.
- [ ] **Step 2:** Suite green. Commit `feat(programs): add Oh My Posh to the catalog`.

### Task 7: `FormatKit.Dev` project, options and module

**Files:**
- Create: `src/FormatKit.Dev/FormatKit.Dev.csproj` (same shape as `FormatKit.Programs.csproj`, InternalsVisibleTo `FormatKit.Dev.Tests`, `FormatKit.Cli.Tests`, `formatkit`), `DevOptions.cs`, `DevEnvironment.cs`, `DevItem.cs`, `DevModule.cs`, `Localization/Strings.cs`, `Resources/Strings.resx`, `Resources/Strings.pt-BR.resx`
- Create: `tests/FormatKit.Dev.Tests/FormatKit.Dev.Tests.csproj` (same shape as the Programs test project), `DevOptionsTests.cs`, `DevItemTests.cs`, `DevModuleTests.cs`
- Modify: `FormatKit.sln`

**Interfaces:**
- Produces:
  - `public sealed record DevEnvironment(string UserProfile, string Documents, string UserName, string ComputerName, int LogicalProcessors, Func<string, string> Expand)` with `static DevEnvironment Current()`.
  - Options records (all `public sealed record`): `DevOptions(WslOptions? Wsl, GitOptions? Git, SshOptions? Ssh, DotfilesOptions? Dotfiles, ShellOptions? Shell, DevDriveOptions? DevDrive)`; `WslOptions(string Distro, string User, WslConfig? Config)`; `WslConfig(string? Memory, int? Processors, string? Swap)`; `GitOptions(string? Name, string? Email, string? DefaultBranch, string? Editor, string? AutoCrlf)`; `SshOptions(string KeyType, string Comment)`; `DotfilesOptions(string? Repo, string? LocalPath, string CloneTo, IReadOnlyList<DotfileEntry> Files)`; `DotfileEntry(string From, string To)` (`To` expanded and absolute); `ShellOptions(WslShellOptions? Wsl, WindowsShellOptions? Windows)`; `WslShellOptions(bool Zsh, bool OhMyZsh, string Theme, IReadOnlyList<string> Plugins)`; `WindowsShellOptions(bool OhMyPosh, string Theme, string Font)`; `DevDriveOptions(string Path, int SizeGB, char Letter, string Label, IReadOnlyList<string> Caches)`.
  - `static (DevOptions? Options, IReadOnlyList<ProfileError> Errors) DevOptions.Parse(JsonElement section, string path, string? baseDirectory, DevEnvironment env)` — every rule of spec section 5 plus R3. Defaults: distro `Ubuntu`; keyType `ed25519`; comment `<UserName>@<ComputerName>` lowercased; cloneTo `%USERPROFILE%\dotfiles` expanded; zsh theme `robbyrussell`, plugins `["git"]`; windows theme `paradox`, font `Meslo`; label `Dev`; caches `[]`. Unknown properties are errors (same as programs). `processors` above `LogicalProcessors` is an error.
  - `internal static class Backup { public static string PathFor(string target, DateTimeOffset localNow); }` → `<target>.formatkit-<yyyyMMdd-HHmmss>.bak` (used by Tasks 8 and 10).
  - `public enum DevItemOutcome { Done, AlreadyInOrder, Pending, NotApplicable, Failed }`; `public sealed record DevItem(string Label, DevItemOutcome Outcome, string? NextAction = null, string? Detail = null)`; `internal static class DevStepResult { public static StepResult From(IReadOnlyList<DevItem> items, bool reboot = false); }` — message per item `"<Label>: <outcome text>"` plus `" — <Detail>"` when present; any Pending/Failed → `StepResult.Fail(<distinct next actions joined by " ">, …)`; else `reboot` → `Reboot`; else `Success`.
  - `public sealed class DevModule(TimeProvider time, DevEnvironment? env = null, IFileSystem? files = null) : IModule`, `Name => "dev"`, overrides with `baseDirectory`; `CreateSteps` returns, for present subsections in order: `WslStep`, `GitStep`, `SshStep`, `DotfilesStep`, `ShellStep`, `DevDriveStep` (constructors defined in Tasks 8–12; until then this task creates none and its test only checks validation). `shell.wsl` passes the `WslOptions` to `ShellStep`.
  - Resource strings (en / pt-BR), used by later tasks:
    | Key | en | pt-BR |
    |---|---|---|
    | `Outcome_Done` | done | feito |
    | `Outcome_AlreadyInOrder` | already in order | já estava em ordem |
    | `Outcome_Pending` | pending | pendente |
    | `Outcome_NotApplicable` | not applicable | não se aplica |
    | `Outcome_Failed` | failed | falhou |
    | `Next_RunWslUser` | Run `formatkit dev wsl-user` when you are at the PC. | Execute `formatkit dev wsl-user` quando estiver no PC. |
    | `Next_RunSshKey` | Run `formatkit dev ssh-key` when you are at the PC. | Execute `formatkit dev ssh-key` quando estiver no PC. |
    | `Next_AddGit` | Add `development.git` to the programs section. | Adicione `development.git` à seção programs. |
    | `Next_AddOhMyPosh` | Add `development.oh-my-posh` to the programs section. | Adicione `development.oh-my-posh` à seção programs. |
    | `Next_EnableVirtualization` | Enable virtualization in the BIOS/UEFI and run again. | Ative a virtualização na BIOS/UEFI e execute novamente. |
    | `Next_WrongAccount` | Run FormatKit from the account that will use WSL. | Execute o FormatKit pela conta que vai usar o WSL. |
    | `Next_OtherCloneTo` | Choose another `cloneTo` folder. | Escolha outra pasta em `cloneTo`. |
    | `Next_OtherVhdx` | Choose another Dev Drive path. | Escolha outro caminho para o Dev Drive. |
    | `Next_UpdateWindows` | Update Windows and run again. | Atualize o Windows e execute novamente. |
    | `Next_SeeLog` | See the log for details. | Veja o log para detalhes. |
    Step titles `Step_Title_Wsl` "Dev: WSL" / "Dev: WSL", `Step_Title_Git` "Dev: Git", `Step_Title_Ssh` "Dev: SSH key" / "Dev: chave SSH", `Step_Title_Dotfiles` "Dev: dotfiles", `Step_Title_Shell` "Dev: terminal", `Step_Title_DevDrive` "Dev: Dev Drive". Validation messages follow the Programs project's `Validation_*` style.

- [ ] **Step 1:** `DevOptionsTests`, one test per rule, named for it — at least: `Wsl_unknown_distro`, `Wsl_user_required`, `Wsl_user_root_rejected`, `Wsl_user_pattern`, `Wslconfig_memory_pattern`, `Wslconfig_processors_above_count`, `Git_requires_a_key`, `Git_email_needs_at`, `Git_autocrlf_values`, `Git_control_characters_rejected`, `Ssh_keytype_values`, `Ssh_comment_default`, `Dotfiles_repo_or_path_exactly_one`, `Dotfiles_repo_scheme`, `Dotfiles_from_with_dotdot_rejected`, `Dotfiles_to_outside_profile_rejected`, `Dotfiles_relative_path_resolves_against_profile`, `Dotfiles_needs_a_file`, `Shell_wsl_requires_wsl`, `Shell_ohmyzsh_requires_zsh`, `Shell_unknown_external_plugin`, `Shell_font_not_in_allowlist` (R3), `DevDrive_size_below_50`, `DevDrive_letter_C_rejected`, `DevDrive_path_must_be_vhdx`, `DevDrive_label_pattern`, `DevDrive_unknown_cache`, `Unknown_property_rejected`, `Empty_enabled_section_valid`. Error paths are asserted (`modules.dev.wsl.user`). `DevItemTests`: `Pending_makes_step_fail_with_its_command`, `NotApplicable_alone_is_success`, `Reboot_without_failures`.
- [ ] **Step 2:** Run; expect failures.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Suite green.
- [ ] **Step 5:** Commit `feat(dev): add dev module options and item results`.

### Task 8: `WslStep` and the WSL user operation

**Files:** Create `src/FormatKit.Dev/Steps/WslStep.cs`, `src/FormatKit.Dev/WslConf.cs`; test `tests/FormatKit.Dev.Tests/Steps/WslStepTests.cs`, `WslConfTests.cs`; register in `DevModule`.

**Interfaces:**
- Consumes: `WslClient` (Task 3), `IInteractiveConsole`, `IUserSession.IsConsoleUser`, `IOptionalFeatures`, `IFileSystem`, `DevItem`.
- Produces:
  - `internal sealed class WslStep(WslOptions options, DevEnvironment env, TimeProvider time) : IStep` — id `dev.wsl`, `RequiresAdmin => true`, `IsCritical => false`.
  - `internal static Task<DevItem> WslStep.EnsureUserAsync(WslOptions options, StepContext ctx, CancellationToken ct)` — used by the `dev wsl-user` command.
  - `internal static class WslConf { public static string SetDefaultUser(string existing, string user); public static string WslConfig(WslConfig config); }` — keep other sections and keys; replace or add `[user]` `default=`; `.wslconfig` is `[wsl2]` with `memory=`, `processors=`, `swap=` lines for the given keys.
- Behavior (spec 6.1, with Task 1 findings): items "WSL", "<distro>", "user <user>", ".wslconfig".
  - `!IsConsoleUser` → every item failed, `Next_WrongAccount`, nothing run.
  - WSL unavailable → `InstallPlatformAsync`; then the reboot signal recorded in Task 1 → result `Reboot` with item "WSL" done and the rest not attempted.
  - Distro missing → `InstallDistroAsync`.
  - User: `id -u` fails → `useradd -m -G sudo -s /bin/bash <user>` as root. Then `passwd -S <user>` second field `L` (locked) → `CanPrompt` ? `RunAttachedAsync(WslClient.ExecSpec(distro, "root", ["passwd", user], Timeouts.Install))` : pending `Next_RunWslUser`. Then `cat /etc/wsl.conf` (missing → empty; this is the constant-script exception list of Global Constraints together with `WslClient.WriteScript` and `command -v` in Task 11), `WslConf.SetDefaultUser`, `WriteFileAsync` as root when changed, `TerminateAsync`.
  - Any WSL result with `IsVirtualizationError` → that item failed with `Next_EnableVirtualization`, later items not attempted.
  - `.wslconfig` differing → existing file moved to `<file>.formatkit-<yyyyMMdd-HHmmss>.bak` (time from `TimeProvider`, local time), then written.

- [ ] **Step 1:** Tests: `Installs_platform_and_requests_reboot`, `Second_run_all_already_in_order`, `Creates_user_and_prompts_password`, `Nobody_at_pc_leaves_password_pending_and_never_attaches` (Review Focus 3), `Wrong_account_fails_without_running_wsl`, `Virtualization_error_fails_with_bios_hint` (Review Focus 1), `Wslconf_keeps_other_sections`, `Wslconfig_backup_name_uses_local_time`, `EnsureUser_is_idempotent_when_password_set` (`passwd -S` → `P`).
- [ ] **Step 2:** Run; expect failures.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Suite green.
- [ ] **Step 5:** Commit `feat(dev): add WSL step`.

### Task 9: `GitStep` and `SshStep`

**Files:** Create `src/FormatKit.Dev/Steps/GitStep.cs`, `SshStep.cs`, `src/FormatKit.Dev/KnownTools.cs`; tests `tests/FormatKit.Dev.Tests/Steps/GitStepTests.cs`, `SshStepTests.cs`.

**Interfaces:**
- Produces:
  - `internal static class KnownTools` with `Git = ["%ProgramFiles%\\Git\\cmd\\git.exe", "%LOCALAPPDATA%\\Programs\\Git\\cmd\\git.exe"]`, `SshKeygen = ["%SystemRoot%\\System32\\OpenSSH\\ssh-keygen.exe"]`, `SshAdd = ["%SystemRoot%\\System32\\OpenSSH\\ssh-add.exe"]`, `OhMyPosh = ["%LOCALAPPDATA%\\Programs\\oh-my-posh\\bin\\oh-my-posh.exe", "%ProgramFiles%\\oh-my-posh\\bin\\oh-my-posh.exe", "%ProgramFiles(x86)%\\oh-my-posh\\bin\\oh-my-posh.exe"]`, and `const string WindowsSsh = "C:/Windows/System32/OpenSSH/ssh.exe"`.
  - `internal sealed class GitStep(GitOptions options) : IStep` — id `dev.git`, `RequiresAdmin => false`. One item per given key (`user.name`, `user.email`, `init.defaultBranch`, `core.editor`, `core.autocrlf`): `git config --global --get <key>` equal → already in order; else `git config --global <key> <value>`. Git not found → every item failed `Next_AddGit`.
  - `internal sealed class SshStep(SshOptions options, DevEnvironment env) : IStep` — id `dev.ssh`, `RequiresAdmin => true`; `internal static Task<IReadOnlyList<DevItem>> SshStep.RunAsync(SshOptions options, DevEnvironment env, StepContext ctx, CancellationToken ct)` shared with `dev ssh-key`. Items "key", "ssh-agent", "agent key", "Git uses Windows SSH". Key file `<UserProfile>\.ssh\id_<type>`; `ssh-keygen -t <type> [-b 4096 for rsa] -C <comment> -f <file>` attached. ssh-agent: `IServiceControl.SetStartTypeAsync("ssh-agent", Automatic)` when not Automatic, then `sc.exe start ssh-agent` (exit 0 or 1056 is success). Agent key: fingerprint from `ssh-keygen -lf <file>.pub` (second field) found in `ssh-add -l` output → already in order; else `CanPrompt` ? attached `ssh-add <file>` : pending `Next_RunSshKey`. Git present → `core.sshCommand` = `KnownTools.WindowsSsh` (same get/set rule as GitStep); Git absent → not applicable. After the items, when the `.pub` file exists, `ctx.Services.Console.WriteLine` the public key and `https://github.com/settings/ssh/new`, and add them as the "key" item's `Detail`.

- [ ] **Step 1:** Tests: `Git_sets_only_differing_keys`, `Git_missing_fails_with_add_git`, `Git_found_through_known_path_when_path_is_stale`; `Ssh_generates_key_attached_with_rsa_bits`, `Ssh_existing_key_kept`, `Ssh_nobody_at_pc_pending_and_never_attaches` (Review Focus 3), `Ssh_agent_set_automatic_and_started`, `Ssh_agent_already_running_1056_ok`, `Ssh_key_already_in_agent`, `Ssh_sets_core_sshcommand`, `Ssh_prints_public_key_and_link`.
- [ ] **Step 2:** Run; expect failures.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Suite green.
- [ ] **Step 5:** Commit `feat(dev): add Git and SSH steps`.

### Task 10: `DotfilesStep`

**Files:** Create `src/FormatKit.Dev/Steps/DotfilesStep.cs`; test `tests/FormatKit.Dev.Tests/Steps/DotfilesStepTests.cs`.

**Interfaces:**
- Consumes: `Backup.PathFor` (Task 7).
- Produces:
  - `internal sealed class DotfilesStep(DotfilesOptions options, TimeProvider time) : IStep` — id `dev.dotfiles`, `RequiresAdmin => false`. Items "repository" then one per file (label = `to`).
  - Repository (`Repo` set): Git via `IToolLocator` (missing → all failed `Next_AddGit`). `CloneTo` missing → `git clone <repo> <cloneTo>`; is a repo whose `git -C <cloneTo> remote get-url origin` equals `Repo` → `git -C <cloneTo> pull --ff-only`; otherwise failed `Next_OtherCloneTo` and file items not attempted. When `CanPrompt`, clone/pull go through `RunAttachedAsync` (Git Credential Manager may prompt); otherwise through `IProcessRunner` with `Environment = { ["GIT_TERMINAL_PROMPT"] = "0" }`. `LocalPath` set → item not applicable, files read from it.
  - File: source `<root>\<From>` missing → failed; target identical by `ReadAllBytes` → already in order; target differs → `Move(target, Backup.PathFor(...))` then `Copy`; target missing → create its folder, `Copy`.

- [ ] **Step 1:** Tests: `Clones_when_missing`, `Pulls_when_same_origin`, `Other_origin_fails_and_skips_files`, `Unattended_clone_disables_terminal_prompt`, `Attended_clone_runs_attached`, `Identical_file_already_in_order`, `Different_file_backed_up_then_copied`, `Crlf_only_difference_is_backed_up_and_replaced` (Review Focus 4), `Missing_source_fails_only_that_file`, `Local_path_uses_folder_in_place`.
- [ ] **Step 2:** Run; expect failures.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Suite green.
- [ ] **Step 5:** Commit `feat(dev): add dotfiles step`.

### Task 11: `ShellStep`

**Files:** Create `src/FormatKit.Dev/Steps/ShellStep.cs`, `src/FormatKit.Dev/ZshRc.cs`, `src/FormatKit.Dev/PowerShellProfileBlock.cs`, `src/FormatKit.Dev/TerminalFragment.cs`; tests for each in `tests/FormatKit.Dev.Tests`.

**Interfaces:**
- Produces:
  - `internal static class ZshRc { public const string Marker = "# Managed by FormatKit"; public static string Render(string template, string theme, IReadOnlyList<string> plugins); public static bool IsManaged(string existing); }` — `Render` puts `Marker` on the first line and replaces the template's `ZSH_THEME="…"` and `plugins=(…)` lines.
  - `internal static class PowerShellProfileBlock { public const string Begin = "# >>> FormatKit oh-my-posh >>>"; public const string End = "# <<< FormatKit oh-my-posh <<<"; public static string? Append(string existing, string initLine); }` — returns null when `existing` already contains `oh-my-posh init`.
  - `internal static class TerminalFragment { public static readonly IReadOnlyDictionary<string, string> FontFamilies; public static string Render(string font); }` — families: Meslo → `MesloLGM Nerd Font`, CascadiaCode → `CaskaydiaCove Nerd Font`, FiraCode → `FiraCode Nerd Font`, JetBrainsMono → `JetBrainsMono Nerd Font`, Hack → `Hack Nerd Font`; JSON updates profiles `{61c54bbd-c2c6-5271-96e7-009a87ff44bf}` (Windows PowerShell) and `{574e775e-4f2a-5b96-ac1e-a2962a402336}` (PowerShell 7), in the form Task 1 Step 4 confirmed.
  - `internal sealed class ShellStep(ShellOptions options, WslOptions? wsl, DevEnvironment env) : IStep` — id `dev.shell`, `RequiresAdmin => false`.
  - WSL items (spec 6.5): distro or user missing → each pending `Next_RunWslUser`. `zsh` present when `["sh","-c","command -v zsh"]` exits 0 (`command` is a shell builtin; the script is constant); install `apt-get update` then `apt-get install -y zsh` as root with `Timeouts.Install`. Oh My Zsh: `test -d ~/.oh-my-zsh` as user (`["test","-d","/home/<user>/.oh-my-zsh"]`), `git` installed via apt when `command -v git` fails, clone `--depth 1 https://github.com/ohmyzsh/ohmyzsh.git /home/<user>/.oh-my-zsh`; external plugins cloned from `https://github.com/zsh-users/<name>.git` into `/home/<user>/.oh-my-zsh/custom/plugins/<name>`. `.zshrc`: `cat` it; missing or `IsManaged` → `Render` from `cat ~/.oh-my-zsh/templates/zshrc.zsh-template` and `WriteFileAsync` as the user, if different; unmanaged → already in order with a warning in the log and a `Detail`. Login shell: `getent passwd <user>` 7th field ≠ zsh path → `chsh -s <path> <user>` as root.
  - Windows items: oh-my-posh via `IToolLocator` (missing → all Windows items failed `Next_AddOhMyPosh`). Font: `IUserContextRunner.RunAsync(oh-my-posh font install <font lowercased>)` with the exit codes Task 1 recorded. Profiles: `<Documents>\PowerShell\Microsoft.PowerShell_profile.ps1` when `%ProgramFiles%\PowerShell\7\pwsh.exe` exists, and `<Documents>\WindowsPowerShell\Microsoft.PowerShell_profile.ps1`, each with the init line Task 1 confirmed for that shell; `Append` null → already in order. Fragment written to `<LOCALAPPDATA>\Microsoft\Windows Terminal\Fragments\FormatKit\formatkit.json` when its content differs; if Task 1 found fragments cannot change the font, this item is not applicable with a `Detail` naming the family to set by hand.

- [ ] **Step 1:** Tests: `ZshRc_sets_theme_and_plugins_and_marker`, `Unmanaged_zshrc_kept`, `Managed_zshrc_rewritten_when_options_change`, `Installs_zsh_and_oh_my_zsh_as_expected_users`, `External_plugins_cloned_from_allowlist`, `Login_shell_changed_once`, `Wsl_side_pending_without_user`, `Profile_block_not_duplicated`, `Profile_with_existing_init_untouched`, `Pwsh7_profile_only_when_installed`, `Fragment_uses_family_name`, `OhMyPosh_missing_fails_with_add_hint`.
- [ ] **Step 2:** Run; expect failures.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Suite green.
- [ ] **Step 5:** Commit `feat(dev): add terminal shell step`.

### Task 12: `DevDriveStep`

**Files:** Create `src/FormatKit.Dev/Steps/DevDriveStep.cs`; test `tests/FormatKit.Dev.Tests/Steps/DevDriveStepTests.cs`.

**Interfaces:**
- Consumes: `IVirtualDisks` (Task 4), `IUserEnvironment`, `IRegistry`, `IFileSystem`.
- Produces: `internal sealed class DevDriveStep(DevDriveOptions options) : IStep` — id `dev.devdrive`, `RequiresAdmin => true`. Items "Dev Drive", "remount at startup" (only when Task 1 found it needed), one per cache.
  - Build: `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion` `CurrentBuildNumber` and `UBR`; below 22621.2338 → all items not applicable `Next_UpdateWindows`.
  - `GetAsync` state attached with `Letter == options.Letter` and `Label == options.Label` → already in order; exists but not attached → `AttachAsync`, then re-check, label differs → failed `Next_OtherVhdx`; letter in use (and not ours) → failed with the letter in `Detail`; `FreeBytes(<path root>) < (SizeGB + 10) GiB` → failed with sizes in `Detail`; then `CreateDevDriveAsync`; `VirtualDiskException` → failed `Next_SeeLog`, cache items not attempted.
  - Caches: `npm` → `npm_config_cache`, `nuget` → `NUGET_PACKAGES`, `pip` → `PIP_CACHE_DIR`, `cargo` → `CARGO_HOME`; folder `<L>:\packages\<name>`; variable equal → already in order; unset → `CreateDirectory` + `Set`; set elsewhere → already in order with `Detail` "kept <value>" and a log warning (spec D7).

- [ ] **Step 1:** Tests: `Old_build_not_applicable`, `Creates_drive_and_sets_caches`, `Second_run_already_in_order`, `Detached_vhdx_attached_again`, `Letter_in_use_fails_before_creating`, `Low_space_fails_before_creating`, `Create_failure_fails_and_skips_caches` (Review Focus 5; `FakeVirtualDisks` throws, asserts no cache set), `Cache_variable_elsewhere_kept`.
- [ ] **Step 2:** Run; expect failures.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Suite green.
- [ ] **Step 5:** Commit `feat(dev): add Dev Drive step`.

### Task 13: CLI, docs and example profile

**Files:** Modify `src/FormatKit.Cli/CliApp.cs` (register `new DevModule(TimeProvider.System, files: env.Services.Files)` after `ProgramsModule`; add `DevCommands`), `src/FormatKit.Cli/FormatKit.Cli.csproj`, `tests/FormatKit.Cli.Tests/FormatKit.Cli.Tests.csproj`, CLI `Resources/Strings*.resx`; create `src/FormatKit.Cli/Commands/DevCommands.cs`, `tests/FormatKit.Cli.Tests/DevCommandsTests.cs`, `profiles/dev-example.json`; modify `README.md`, `docs/testing.md`.

**Interfaces:**
- Produces: `dev wsl-user [--profile <file>]` and `dev ssh-key [--profile <file>]` (spec 8). Without a profile: `WslOptions("Ubuntu", <Windows user name lowercased>, null)` when it matches the user pattern, else exit 2 asking for `--profile`; `SshOptions("ed25519", <default comment>)`. Console redirected (`!CanPrompt`) → exit 2. Item failed or pending → 1; else 0. `dev ssh-key` relaunches elevated like `run` (it sets the ssh-agent start type). Both print each `DevItem` line.

- [ ] **Step 1:** Tests: `Dev_module_registered_after_programs`, `WslUser_redirected_console_exit_2`, `WslUser_without_profile_uses_windows_user`, `SshKey_reads_profile_ssh_section`, `Dev_commands_exit_1_on_pending`.
- [ ] **Step 2:** Run; expect failures.
- [ ] **Step 3:** Implement. README: replace "dev environment" in the planned line; add a "Dev environment" section with the profile of spec 5, the developer cleanup ids, the zsh / Oh My Posh suggestions as copy-ready snippets, the CRLF note for dotfiles, and the two commands. `docs/testing.md`: scenarios 13–16 from spec section 10 with exact commands and expected results. `profiles/dev-example.json`: a full example that passes validation (add a test that loads it, next to the existing example-profile test if there is one).
- [ ] **Step 4:** Suite green.
- [ ] **Step 5:** Commit `feat(cli): add dev commands and register the dev module` and `docs: document the dev environment`.

### Task 14: VM scenarios 13–16 (controller, no subagent)

- [ ] **Step 1:** Publish (`dotnet publish src/FormatKit.Cli -c Release`) and copy the exe and test profiles to the VM.
- [ ] **Step 2:** Run scenarios 13–16 exactly as `docs/testing.md` states, typing passwords in the VM console when prompted (the user does this step if a password must be typed live; credentials never enter chat).
- [ ] **Step 3:** Fix any finding with a test first; commit `fix(dev): address VM scenario findings`.
- [ ] **Step 4:** Report results to the user; merging waits for their explicit approval.
