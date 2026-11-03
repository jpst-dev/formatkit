# Capture a machine into a profile (sub-project 2)

## Goal

Before formatting, a user runs one command on the old Windows and gets a profile that rebuilds what this machine has, instead of writing that profile by hand.

```
formatkit profile capture <file> [--force]
```

Success means: the file is valid for `formatkit run`, `media build` and `prepare check` once the user makes the one decision reserved for them (agreements, below), it holds no secret, and a report says what was captured, what was not and why, and what deserves a second look.

## Decisions

The user delegated these under the standing authorization of 2026-11-03. Each records the reason, so it can be challenged.

| # | Decision | Reason |
|---|---|---|
| D1 | The command is read-only and does not elevate. | It runs on the machine the user still depends on. Anything needing admin is reported as not checked. |
| D2 | One self-contained JSON file, no side files. | The profile must survive the format on its own; `importFile` would add a second file to lose. |
| D3 | `programs.acceptAgreements` is written as `false`. | Accepting winget and Store agreements is the user's decision. The first `run` rejects the profile with the existing message until they set it; the report says so up front. |
| D4 | Validation runs before writing; if it fails nothing is written. | A capture that produces a broken profile is discovered after the format, when it is too late. Validation runs on a copy with `acceptAgreements: true`, so D3 does not fail it. |
| D5 | `cleanup` lists applied items in `include` and has no `level`. | A level would also select items the machine does not have. `include` is exact, and the module already accepts a section with `include` and no `level`. |
| D6 | Aggressive items are never captured; they are reported. | They need `confirmAggressive`, a confirmation the user must give. |
| D7 | Contested items that are applied are captured and listed under review. | They are on this machine, so they reflect it, but the catalog flags them as debatable. |
| D8 | Versions are not pinned for programs. | A reinstall wants current versions. |
| D9 | Runtime and redistributable packages, and packages that ship with Windows, are skipped and counted in the report. | They arrive as dependencies or with Windows; listing them only adds failures. |
| D10 | `setup.account` and `setup.edition` are not captured; `account` stays at its default and both are listed under review. | The account type of a future install is a choice, not a fact about this PC. The edition must exist in the ISO. |
| D11 | Dotfiles are not captured; they are reported. | A repository address cannot be inferred from the files. |
| D12 | The app page is out of scope. | The CLI is enough to use the feature and to generate the profile for the physical-machine test. A page can follow on the same `ProfileCapture` flow. |
| D13 | Each module captures its own section; `ProfileCapture` in Hosting assembles them. There is no new project. | Each capture needs its module's internals (catalog handlers, font map, validation patterns). This mirrors how modules own `Validate`. |

## Behavior

1. Resolve `<file>`. An invalid path exits `InvalidInput` (2). An existing file without `--force` exits `InvalidInput` and leaves it untouched.
2. Run each section capture. A source that fails affects only its own entries: they become notes and the capture continues.
3. Assemble the profile, then validate it (D4). A validation failure prints the errors, writes nothing and exits `CompletedWithFailures` (1): it is a FormatKit bug, not a user error.
4. Write with `Files.WriteAllTextAtomic`, UTF-8, indented.
5. Print the report. Warn when the file is on the Windows drive (`WindowsVolume.IsLost`), because the format erases it, and warn that the file holds personal data (name, e-mail, computer name), so it should not be published as is.
6. Exit `Ok` (0), even if some sources were not available. Ctrl+C exits `Cancelled` (130) and writes nothing.

`name` is the computer name. Every module section is always present with its `enabled` flag, so the user sees what is off and can flip it.

## What is captured

### `modules.programs`

Source: `winget export` to a temporary file that is always deleted. The existing `WingetClient.ExportAsync` passes `--accept-source-agreements`, which capture must not do, so it gains a parameter `acceptSourceAgreements` (default `true`, keeping `programs export` as it is) and capture passes `false`. If winget refuses because the agreements were never accepted, the section stays disabled and the note tells the user to run `winget list` once and accept them. The VM scenarios record how winget behaves in that state, including whether it returns a partial list.

- A package whose id and source match a catalog program becomes a `catalog` id. Any other becomes a `winget` entry with `id` and `source`, no version (D8).
- Skipped packages (D9) come from a list in the Programs project. Its starting content comes from what `winget export` prints on the clean VM, plus these patterns: `Microsoft.VCRedist.*`, `Microsoft.VCLibs*`, `Microsoft.UI.Xaml*`, `Microsoft.WindowsAppRuntime*`, `Microsoft.DotNet.*Runtime*`, `Microsoft.DotNet.DesktopRuntime*`, `Microsoft.Edge`, `Microsoft.EdgeWebView2Runtime`, `Microsoft.AppInstaller`.
- `enabled` is true when anything was captured. `acceptAgreements` is false (D3). `upgrade` is left out.
- winget missing or failing: `enabled: false`, and a not-captured note with the reason.

### `modules.cleanup`

`CleanupCapture` asks `ItemApplier.IsAppliedAsync` for every catalog item:

- Applied, not aggressive: goes into `include`, in catalog order.
- Applied, aggressive: note under review, not captured (D6).
- Applied, contested: captured, and noted under review (D7).
- A check that throws (for example an action that needs admin): note as not checked. Only `OperationCanceledException` passes through.

`enabled` is true when `include` has any item. Items that share a power scheme or power setting are captured as they are: `ItemSelector` already keeps one at run time. `confirmAggressive` is never written.

### `modules.drivers`

Fixed: `{ "enabled": true, "sources": ["windowsUpdate", "oem", "gpu"] }`. Drivers are detected at run time on the new install, so there is nothing to read from this machine.

### `modules.dev`

Each part is read independently. A part with nothing to report is left out. `enabled` is true when any part is present.

- **git**: `git config --global --get` for `user.name`, `user.email`, `init.defaultBranch`, `core.editor`, `core.autocrlf`. Git not found: not captured. Values the parser would reject are dropped and noted.
- **ssh**: `keyType` and `comment` from `id_ed25519.pub`, else `id_rsa.pub`, in `%USERPROFILE%\.ssh`. A comment outside the profile's pattern is left out, so the default applies. The note says the key itself is not captured: a new one is generated, or the user restores theirs. **Private key files are never opened**; a test pins it.
- **wsl**: `ListDistrosAsync`; the first distro in the allowed list. Other distros get a not-captured note. The user comes from `whoami` through `ExecAsync`, read-only. `.wslconfig` values `memory`, `processors` and `swap` in `[wsl2]`, dropped and noted if invalid. WSL commands that fail or time out leave a note.
- **shell.windows**: `ohMyPosh` true when the executable is found and a PowerShell profile has an `oh-my-posh init` line. `theme` is the value of `--config` when it matches the theme pattern (a path or URL is reduced to its file name without `.omp.json`, and noted). `font` is the first entry of `TerminalFragment.FontFamilies` installed per `IsFontInstalled`; other Nerd Fonts are not detected.
- **shell.wsl**: from the captured distro: `zsh` if `command -v zsh` succeeds, `ohMyZsh` if `~/.oh-my-zsh` exists, `theme` and `plugins` from `~/.zshrc`, dropping what the parser would reject.
- **devDrive**: the first volume from `IVolumes` with a `BackingVhdx`, read with `IVirtualDisks.GetAsync`. `VirtualDiskState` gains an optional `long? SizeBytes` read from `Get-DiskImage`. `caches` comes from user environment variables that point into the drive. Not captured, with a note: a VHDX on the Windows drive (the format would erase it), a size under the 50 GB minimum, a letter or label the parser would reject, or an unknown size. Size is rounded down to whole GB.
- **dotfiles**: never; note (D11).

### `setup`

From `MachineLocale.Current`: `language`, `keyboard`, `timeZone`, and `computerName` from the environment when it matches the 1-15 pattern. Anything `SetupSectionParser` would reject is dropped and noted. `account` and `edition` per D10.

## Components

```
Core       CaptureSection(JsonObject? Value, IReadOnlyList<CaptureNote> Notes)
           CaptureNote(CaptureNoteKind Kind, string Path, string Text)   Kind: NotCaptured | Review | Info
Programs   ProgramsCapture.RunAsync(services, catalog, ct) -> CaptureSection
Cleanup    CleanupCapture.RunAsync(services, catalog, log, ct) -> CaptureSection
Dev        DevCapture.RunAsync(services, environment, ct) -> CaptureSection  (one small reader per part)
Drivers    a constant section, built in ProfileCapture
Media      SetupCapture.Run(services, computerName) -> CaptureSection
Hosting    ProfileCapture: assembles, validates (ProfileLoader + SetupSectionParser), writes
Cli        ProfileCommands: `profile capture`, prints the report
```

Everything reaches the system through `SystemAccess`. User-facing text lives in each project's resources, in English and pt-BR.

## Report

Three groups, in this order, each line with the profile path it concerns:

- **Captured**: one summary line per section (for example, "programs: 12 from the catalog, 8 from winget, 14 runtimes skipped").
- **Not captured**: entry and reason.
- **Review**: what needs a decision, always including `programs.acceptAgreements` when programs were captured.

## Error handling

- A failing source never aborts the capture (Behavior 2). Failures are logged with their detail; the report shows the reason.
- No secret is read: no private key, no credential helper output, no environment variable beyond the known cache names. `git config` reads only the five keys above.
- A write failure prints the reason and exits `CompletedWithFailures`.

## Testing

- Unit tests with fakes for every source, including: source missing, command failing, invalid values dropped, and an end-to-end test that a capture with every section filled passes `ProfileLoader` and `SetupSectionParser`.
- A test that a capture only reads: the fakes record no registry write, no service or file change other than the output file.
- A test that no path under `.ssh` other than the `.pub` files is opened.
- Resource parity tests already cover the new strings.
- VM scenarios in `docs/testing.md`, because text-only tests of real tool output have already hidden a bug (the `BusType` cast in PR 9):
  - Clean VM: capture, inspect the output and the winget noise list, and confirm `run --dry-run` works after setting `acceptAgreements`.
  - Configured VM (run a dev profile first, including the Dev Drive and WSL when available): capture, compare against the source profile.
  - Round trip: `prepare check --profile` and `media build`'s profile parsing accept the captured file.

## Out of scope

- The app page (D12).
- Capturing files or data: this is configuration only. `prepare check` already covers data at risk.
- Comparing a capture to an existing profile, merging, or updating one.
- Winget `Context` and `Scope` for each package: `winget export` does not report them.

## Found while running

To be filled in by the VM scenarios.
