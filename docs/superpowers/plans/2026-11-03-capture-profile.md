# Capture Profile Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `formatkit profile capture <file> [--force]` writes a valid, secret-free profile of the running machine and reports what it captured, skipped and wants reviewed.

**Architecture:** Each module captures its own section into a `CaptureSection` (JSON plus notes). `ProfileCapture` in Hosting assembles, validates and writes; `ProfileCommands` in the CLI prints the report. Everything is read-only and goes through `SystemAccess`.

**Tech Stack:** .NET 10, System.Text.Json (`JsonObject`), System.CommandLine, xunit.v3, fakes in `tests/FormatKit.Core.Tests/Fakes`.

**Spec:** `docs/superpowers/specs/2026-11-03-capture-profile-design.md`

## Global Constraints

- The command does not elevate and changes nothing except the output file and a deleted temp file.
- `programs.acceptAgreements` is always written as `false`; `confirmAggressive` is never written.
- Capture never passes `--accept-source-agreements`.
- Private key files are never opened; only `id_ed25519.pub` and `id_rsa.pub`.
- A failing source never aborts the capture; only `OperationCanceledException` passes through.
- User-facing text goes in `Resources/Strings.resx` and `Strings.pt-BR.resx` of the owning project.
- Unit tests: `dotnet test -- --filter-not-trait "Category=Integration"`. Never run system-changing commands or winget agreements on the host.
- Commits: conventional, ending in `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. Non-ASCII values (`João`, `Ação`): written literally in UTF-8, not `\u` escaped, and loaded back equal. Git output must decode correctly. Test in Tasks 4 and 7; checked for real in the VM.
2. A fresh PC where nothing is found: the profile is still valid with only drivers enabled. Test in Task 7.
3. The output folder does not exist: reported as a write failure, exit 1, no stack trace. Test in Task 8.
4. WSL with no distro, or a distro whose default user is `root`: no `wsl` part, or a note and no `user`. Test in Task 5.
5. The temp export file is deleted even when winget fails or the run is cancelled. Test in Task 2.

---

### Task 1: Core types and system access additions

**Files:**
- Create: `src/FormatKit.Core/Capture/CaptureSection.cs`
- Modify: `src/FormatKit.Core/SystemAccess/WingetClient.cs:141`, `WslClient.cs`, `IVirtualDisks.cs`, `PowerShellVirtualDisks.cs`
- Test: `tests/FormatKit.Core.Tests/SystemAccess/WingetClientTests.cs`, `WslClientTests.cs`, `PowerShellVirtualDisksTests.cs`

**Interfaces:**
- Produces:
  - `enum CaptureNoteKind { NotCaptured, Review, Info }`
  - `record CaptureNote(CaptureNoteKind Kind, string Path, string Text)`
  - `record CaptureSection(JsonObject Value, IReadOnlyList<CaptureNote> Notes, string Summary)`
  - `WingetClient.ExportAsync(string outputFile, CancellationToken ct, bool acceptSourceAgreements = true)`
  - `WslClient.ExecAsDefaultUserAsync(string distro, IReadOnlyList<string> command, CancellationToken ct)`
  - `VirtualDiskState(bool Attached, char? Letter, string? Label, long? SizeBytes = null)` (keep the existing parameter types)

- [ ] **Step 1: Write failing tests**
  - `Export_can_leave_source_agreements_alone`: with `acceptSourceAgreements: false`, arguments do not contain `--accept-source-agreements` and contain `--disable-interactivity`. The default still contains it.
  - `ExecAsDefaultUser_passes_no_user`: arguments contain `-d Ubuntu` and no `-u`.
  - `Get_parses_size`: stdout `{"Attached":true,"Letter":"D","Label":"Dev","Size":107374182400}` gives `SizeBytes == 107374182400`; `Get_script_reads_the_image_size` asserts the script emits `Size` from `$img.Size`.
- [ ] **Step 2: Run them**: `dotnet test tests/FormatKit.Core.Tests -- --filter-not-trait "Category=Integration"`. Expected: compile failure.
- [ ] **Step 3: Implement.** Add the types and parameters above; extend `GetScript` output with `Size = $img.Size`. Older JSON without `Size` gives null.
- [ ] **Step 4: Run them.** Expected: PASS, and existing tests unchanged.
- [ ] **Step 5: Commit** `feat(core): add capture types and read-only system queries`.

### Task 2: Programs capture

**Files:**
- Create: `src/FormatKit.Programs/Capture/ProgramsCapture.cs`, `src/FormatKit.Programs/Capture/SkippedPackages.cs`
- Modify: `src/FormatKit.Programs/Resources/Strings*.resx`
- Test: `tests/FormatKit.Programs.Tests/Capture/ProgramsCaptureTests.cs`

**Interfaces:**
- Consumes: Task 1. `WingetImportFile.Read(json, warn)`, `ProgramCatalog`.
- Produces: `static Task<CaptureSection> ProgramsCapture.RunAsync(ServiceSet services, ProgramCatalog catalog, string tempDirectory, Serilog.ILogger log, CancellationToken ct)`; `static bool SkippedPackages.IsSkipped(string wingetId)`.

- [ ] **Step 1: Write failing tests**
  - `Maps_catalog_programs_and_keeps_the_rest_as_winget`: a package matching a `CatalogProgram.WingetId` (case-insensitive, same source) goes to `catalog`; another to `winget` with only `id` and `source`.
  - `Never_accepts_agreements`: `acceptAgreements` is `false` and the winget call lacks `--accept-source-agreements`.
  - `Skips_runtime_packages_and_counts_them`: `Microsoft.VCRedist.2015+.x64`, `Microsoft.DotNet.DesktopRuntime.8`, `Microsoft.UI.Xaml.2.8`, `Microsoft.AppInstaller`, `Microsoft.Edge` are absent, and `Summary` carries the count.
  - `Winget_missing_disables_the_section_with_a_note`; `Export_failure_disables_the_section_and_explains_the_agreements`.
  - `Temp_file_is_deleted_on_success_failure_and_cancel` (Review Focus 5).
  - `Empty_export_disables_the_section`.
- [ ] **Step 2: Run them.** Expected: compile failure.
- [ ] **Step 3: Implement.** Skip patterns exactly as in the spec, matched case-insensitively with `*` as a wildcard. Temp file named with a GUID in `tempDirectory`, removed in `finally`.
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(programs): capture installed programs into a section`.

### Task 3: Cleanup capture

**Files:**
- Create: `src/FormatKit.Cleanup/Capture/CleanupCapture.cs`
- Modify: `src/FormatKit.Cleanup/Resources/Strings*.resx`
- Test: `tests/FormatKit.Cleanup.Tests/Capture/CleanupCaptureTests.cs`

**Interfaces:**
- Consumes: Task 1. `ItemApplier.IsAppliedAsync` through a `Func<CleanupItem, Task<bool>>` seam, so tests need no handlers. `ActionHandlers`, `JournalStore` as `CleanupModule.CreateSteps` builds them.
- Produces: `static Task<CaptureSection> CleanupCapture.RunAsync(ServiceSet services, CleanupCatalog catalog, AppPaths paths, Serilog.ILogger log, CancellationToken ct)`; internal overload taking the seam.

- [ ] **Step 1: Write failing tests**
  - `Applied_items_go_to_include_in_catalog_order`, with no `level`.
  - `Applied_aggressive_item_is_reported_not_captured`, and `confirmAggressive` absent.
  - `Applied_contested_item_is_captured_and_flagged_for_review`.
  - `A_check_that_throws_becomes_not_checked`; `Cancellation_passes_through`.
  - `Nothing_applied_disables_the_section`.
  - `Captured_section_passes_module_validation`: `CleanupModule.Validate` returns no errors, including with an opt-in and a developer item in `include`.
- [ ] **Step 2: Run them.** Expected: compile failure.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(cleanup): capture applied catalog items into a section`.

### Task 4: Dev capture: git and SSH

**Files:**
- Create: `src/FormatKit.Dev/Capture/DevCapture.cs`, `GitReader.cs`, `SshReader.cs`, `CaptureParts.cs`
- Modify: `src/FormatKit.Dev/DevOptions.cs` (make the validation patterns and allowed lists `internal`), `Resources/Strings*.resx`
- Test: `tests/FormatKit.Dev.Tests/Capture/GitReaderTests.cs`, `SshReaderTests.cs`, `DevCaptureTests.cs`

**Interfaces:**
- Consumes: Task 1. `KnownTools.Git`, `DevEnvironment`, `DevOptions.Parse`.
- Produces:
  - `record CapturePart(JsonObject? Value, IReadOnlyList<CaptureNote> Notes)`
  - `static Task<CaptureSection> DevCapture.RunAsync(ServiceSet services, DevEnvironment env, Serilog.ILogger log, CancellationToken ct)`, which calls readers in order and sets `enabled`.
  - `static Task<CapturePart> GitReader.ReadAsync(ServiceSet, CancellationToken)`, `static CapturePart SshReader.Read(ServiceSet, DevEnvironment)`.

- [ ] **Step 1: Write failing tests**
  - Git: `Reads_the_five_keys`, asserting exactly `user.name`, `user.email`, `init.defaultBranch`, `core.editor`, `core.autocrlf` are queried with `config --global --get`; `Missing_key_is_left_out` (exit 1); `Git_not_found_gives_a_note`; `Invalid_branch_is_dropped_with_a_note`; `Keeps_non_ascii_name` with `João`.
  - SSH: `Prefers_ed25519`; `Falls_back_to_rsa`; `Comment_outside_the_pattern_is_left_out`; `No_key_gives_no_part`; `Never_opens_a_private_key`: seed `id_ed25519` and assert the recording file system read only `.pub` paths under `.ssh`; `Note_says_the_key_is_not_captured`.
  - DevCapture: `Dotfiles_are_reported_not_captured`; `Nothing_found_disables_the_section`.
- [ ] **Step 2: Run them.** Expected: compile failure.
- [ ] **Step 3: Implement.** Check how `GitStep` starts git and reuse its encoding handling.
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(dev): capture git and SSH settings`.

### Task 5: Dev capture: WSL

**Files:**
- Create: `src/FormatKit.Dev/Capture/WslReader.cs`, `WslConfigReader.cs`
- Modify: `DevCapture.cs`, `Resources/Strings*.resx`
- Test: `tests/FormatKit.Dev.Tests/Capture/WslReaderTests.cs`, `WslConfigReaderTests.cs`

**Interfaces:**
- Consumes: Task 1 `ExecAsDefaultUserAsync`, Task 4 `CapturePart`.
- Produces: `record WslCapture(string? Distro, string? User, JsonObject? WslPart, JsonObject? ShellPart, IReadOnlyList<CaptureNote> Notes)`, `static Task<WslCapture> WslReader.ReadAsync(ServiceSet, DevEnvironment, CancellationToken)`, `static CapturePart WslConfigReader.Read(ServiceSet, DevEnvironment)`.

- [ ] **Step 1: Write failing tests**
  - `First_allowed_distro_wins_and_others_get_a_note`: distros `Fedora`, `Ubuntu-22.04`, `Ubuntu` picks `Ubuntu`.
  - `No_distro_gives_no_part`; `Wsl_failure_gives_a_note` (Review Focus 4).
  - `Root_user_is_dropped_with_a_note` (Review Focus 4); `User_comes_from_whoami`.
  - Shell: `Reads_zsh_ohmyzsh_theme_and_plugins`, parsing `ZSH_THEME="agnoster"` and `plugins=(git docker)`; a theme or plugin the parser rejects is dropped with a note; `Reserved_theme_name_is_dropped`.
  - `.wslconfig`: `Reads_memory_processors_swap`; `Invalid_memory_is_dropped_with_a_note`; `Keys_outside_wsl2_section_are_ignored`; `Missing_file_gives_nothing`.
- [ ] **Step 2: Run them.** Expected: compile failure.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(dev): capture WSL and its shell`.

### Task 6: Dev capture: Windows shell and Dev Drive

**Files:**
- Create: `src/FormatKit.Dev/Capture/WindowsShellReader.cs`, `DevDriveReader.cs`
- Modify: `DevCapture.cs`, `Steps/ShellStep.cs` (make `IsFontInstalled` internal), `Resources/Strings*.resx`
- Test: `tests/FormatKit.Dev.Tests/Capture/WindowsShellReaderTests.cs`, `DevDriveReaderTests.cs`

**Interfaces:**
- Consumes: Task 1 `SizeBytes`, Task 4 `CapturePart`, `TerminalFragment.FontFamilies`, `WindowsVolume.IsLost`, `DevOptions` cache variable names.
- Produces: `static CapturePart WindowsShellReader.Read(ServiceSet, DevEnvironment, Serilog.ILogger)`, `static Task<CapturePart> DevDriveReader.ReadAsync(ServiceSet, string systemDrive, CancellationToken)`.

- [ ] **Step 1: Write failing tests**
  - Shell: `Needs_the_exe_and_an_init_line`; `Theme_from_plain_name`; `Theme_from_path_is_reduced_and_noted`, with `$env:POSH_THEMES_PATH\paradox.omp.json` giving `paradox`; `Theme_from_url_is_reduced`; `First_installed_known_font`; `Reads_both_powershell_profile_folders`.
  - Dev Drive: `Reads_path_letter_label_size`, with 107374182400 bytes giving `sizeGB: 100`; `Size_rounds_down`; `Vhdx_on_windows_drive_gets_a_note`; `Under_50GB_gets_a_note`; `Unknown_size_gets_a_note`; `Caches_from_env_vars_pointing_into_the_drive`, where a variable pointing elsewhere is ignored; `No_vhdx_gives_no_part`; `Volumes_failure_gives_a_note`.
  - DevCapture: `Full_capture_passes_DevOptions_Parse`, with every part filled.
- [ ] **Step 2: Run them.** Expected: compile failure.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(dev): capture the Windows shell and Dev Drive`.

### Task 7: Setup capture and ProfileCapture

**Files:**
- Create: `src/FormatKit.Media/Setup/SetupCapture.cs`, `src/FormatKit.Hosting/ProfileCapture.cs`
- Modify: `src/FormatKit.Media/Resources/Strings*.resx`, `src/FormatKit.Hosting/Resources/Strings*.resx`
- Test: `tests/FormatKit.Media.Tests/Setup/SetupCaptureTests.cs`, `tests/FormatKit.Hosting.Tests/ProfileCaptureTests.cs`

**Interfaces:**
- Consumes: Tasks 1-6. `MachineLocale.Current`, `SetupSectionParser.Parse`, `ProfileLoader.Parse`, `Files.WriteAllTextAtomic`, `WindowsVolume`.
- Produces:
  - `static CaptureSection SetupCapture.Run(IRegistry registry, string computerName)`
  - `record ProfileCaptureResult(string? Json, IReadOnlyList<CaptureSummary> Sections, IReadOnlyList<CaptureNote> Notes, IReadOnlyList<ProfileError> Errors)`, `record CaptureSummary(string Section, string Text)`
  - `sealed class ProfileCapture(HostEnvironment env, IReadOnlyList<IModule> modules, ProgramCatalog programs, CleanupCatalog cleanup, DevEnvironment dev)`, with `Task<ProfileCaptureResult> RunAsync(CancellationToken)` and `CaptureWrite Write(ProfileCaptureResult, string path)`
  - `record CaptureWrite(bool OnWindowsDrive, string? Error)`

- [ ] **Step 1: Write failing tests**
  - Setup: `Reads_locale_and_name`; `Long_or_invalid_computer_name_is_dropped_with_a_note`; `Null_keyboard_is_left_out`; `Account_and_edition_are_absent_and_flagged_for_review`.
  - Assembly: `Output_has_every_module_with_its_enabled_flag`; `Name_is_the_computer_name`; `Drivers_section_is_fixed`.
  - `Empty_machine_is_still_valid` (Review Focus 2).
  - `Validates_with_agreements_accepted_but_keeps_false_in_the_output`.
  - `Invalid_assembly_returns_errors_and_no_json`.
  - `Non_ascii_is_written_literally_and_round_trips` (Review Focus 1): output contains `João`, not `ã`.
  - `Only_reads`: fakes record no registry write, no service change, and no file other than the temp export.
  - `Agreements_are_always_flagged_when_programs_were_captured`.
- [ ] **Step 2: Run them.** Expected: compile failure.
- [ ] **Step 3: Implement.** Serialize with `WriteIndented` and `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`. Validation clones the JSON and sets `modules.programs.acceptAgreements` to true before calling `ProfileLoader.Parse`.
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(hosting): assemble and validate a captured profile`.

### Task 8: The command and the docs

**Files:**
- Create: `src/FormatKit.Cli/Commands/ProfileCommands.cs`
- Modify: `src/FormatKit.Cli/CliApp.cs:91`, `src/FormatKit.Cli/Resources/Strings*.resx`, `README.md`, `docs/testing.md` (new scenarios before "Hardware-only checklist"), `site/src/content/docs/guide/before.mdx` and its English counterpart, the sub-project 2 line in `docs/superpowers/specs/2026-10-29-windows-settings-design.md` is left as written
- Test: `tests/FormatKit.Cli.Tests/ProfileCommandsTests.cs`

**Interfaces:**
- Consumes: Task 7.
- Produces: `static Command ProfileCommands.Create(CommandContext)`, `profile capture <file> [--force]`.

- [ ] **Step 1: Write failing tests**
  - `Existing_file_without_force_exits_2_and_is_untouched`; `Force_overwrites`; `Bad_path_exits_2`.
  - `Missing_folder_exits_1_with_a_message` (Review Focus 3).
  - `Prints_captured_not_captured_and_review_in_that_order`.
  - `Warns_on_the_windows_drive_and_about_personal_data`.
  - `Validation_errors_exit_1_and_write_nothing`; `Does_not_ask_for_elevation`; `Cancel_exits_130_and_writes_nothing`.
- [ ] **Step 2: Run them.** Expected: compile failure.
- [ ] **Step 3: Implement** the command, register it after `PrepareCommands`, and write the docs: README section, the two-language site guide, and three scenarios in `docs/testing.md` as listed in the spec's Testing section.
- [ ] **Step 4: Run the whole suite:** `dotnet test -- --filter-not-trait "Category=Integration"`. Expected: all pass, with the resource parity tests covering the new strings.
- [ ] **Step 5: Commit** `feat(cli): add profile capture`.

### Task 9: VM scenarios

**Files:**
- Modify: `src/FormatKit.Programs/Capture/SkippedPackages.cs`, the spec's "Found while running", any fix found (each with a pinning unit test).

- [ ] **Step 1: Publish** `dotnet publish src/FormatKit.Cli -c Release -o artifacts`, then `vm.ps1 restore`, wait for `fktest`, copy.
- [ ] **Step 2: Clean VM, agreements never accepted.** Run capture as the signed-in user with `vm.ps1 job`. Record how winget behaves and whether the list is partial. Expected: a valid file, no agreement accepted, exit 0.
- [ ] **Step 3: Clean VM, agreements accepted** with `winget list` in the VM. Capture and read the package list; add what ships with Windows to `SkippedPackages`, with a test.
- [ ] **Step 4: Configured VM.** Run a dev profile with git (name `João`), SSH, Oh My Posh, cleanup items and, with `nested`, WSL. Capture and compare with the source profile. Expected: values match and non-ASCII is intact.
- [ ] **Step 5: Round trip.** Set `acceptAgreements` to true in the VM, then expect `run --dry-run`, `prepare check --profile` and `media build`'s parser to accept the file.
- [ ] **Step 6: Record** results in the spec and commit `docs: record the capture VM runs`.
