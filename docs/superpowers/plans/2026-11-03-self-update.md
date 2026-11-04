# Update from inside FormatKit: implementation plan

> Executed inline with superpowers:executing-plans, as in sub-project 2. Steps use checkboxes.

**Goal:** the window offers a newer release at start and installs it on a click; `formatkit update` does the same on request.

**Spec:** `docs/superpowers/specs/2026-11-03-self-update-design.md`

## Global constraints

- TDD: each test is watched failing first. No warnings: `TreatWarningsAsErrors`.
- All file work goes through `IFileSystem`, and text lives in `Strings.resx` and `Strings.pt-BR.resx`.
- Never run a real update on the host: real swaps run only in the VM.

## Review focus

- A failure after the first swap with two executables: both originals must come back.
- The running executable's `.old` from an earlier update is locked: the update must stop before touching anything.
- A release lacking `SHA256SUMS.txt` or one executable, or with an address outside the repository.
- The window opened by the resume task, or with a saved run state: no check, no banner.
- The new process started while the old one holds the instance mutex.

## Task 1: versions and the release source (Core)

**Produces:** `AppVersion.TryParse(string?, out AppVersion?)`, `IComparable<AppVersion>`, `Text`; `ReleaseAsset(string Name, Uri Url, long Size)`; `ReleaseInfo(string Tag, Uri Page, IReadOnlyList<ReleaseAsset> Assets)`; `IReleaseSource.GetLatestAsync(CancellationToken)`; `GitHubReleaseSource(string version)`.

- [ ] Tests: `0.2.0 < 0.2.1 < 0.3.0 < 1.0.0`; `0.2.0-rc.1 < 0.2.0`; `rc.2 < rc.10`; `v` prefix and `+commit` are ignored; bad text fails. The source parses a sample answer, sends the User-Agent, and throws on a bad status or bad JSON.
- [ ] Implement, watch them pass, commit.

## Task 2: `Updater` (Hosting)

**Consumes:** Task 1. **Produces:** `CheckAsync`, `Refusal()`, `UpdateAsync(ReleaseInfo, CancellationToken)`, static `RemoveLeftovers(IFileSystem, string exePath)`, records `UpdateCheck` and `UpdateResult`.

- [ ] Tests for every case in the spec's Testing section and this plan's Review focus. `InMemoryFileSystem` gains a move failure hook, and `FakeHttpDownloader` gains content per address.
- [ ] Implement, watch them pass, commit.

## Task 3: `formatkit update` (CLI)

**Consumes:** Task 2.

- [ ] Tests in `CliAppTests` for the spec's exit code table.
- [ ] Implement, register it in `CliApp`, call `RemoveLeftovers` in `Program.cs`, commit.

## Task 4: the window (App)

**Consumes:** Task 2. **Produces:** `UpdateViewModel` with `CheckOnStartAsync`, `CheckCommand`, `UpdateCommand`, `CheckForUpdates`, `IsUpdating`, `RestartRequested`.

- [ ] Tests: no check with the setting off or a refusal; a silent failure at start and a visible one from About; update blocked during work; a run blocked during an update; settings keep `lastProfile`; Home's banner yields to an interrupted run.
- [ ] Implement, wire Home, About and `App.xaml.cs` (release the instance before starting the new process), commit.

## Task 5: docs

- [ ] README, `docs/testing.md` scenarios, site install page in both languages, CLI page if it lists commands. Commit.

## Task 6: final review and VM

- [ ] Whole-branch review on the most capable model; one fix pass for Critical and Important findings.
- [ ] VM: spec's Testing section. Record it in "Found while running".
