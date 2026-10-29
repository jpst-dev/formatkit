# FormatKit — WPF UI Design

- **Date:** 2026-10-29
- **Status:** Approved in away mode (2026-10-29): the user answered every
  scoping question and approved design sections 1-3 (architecture, screens,
  installation media screen) in conversation, then left with the away-mode
  instruction from `CLAUDE.local.md` before approving section 4 (error
  handling). Section 4 onwards and the written spec itself follow Claude's
  recommendations and await the user's review (see "Decided in away mode")
- **Scope:** Sub-project 7 (WPF interface), version 1: a graphical app over the
  existing core that runs a profile, shows the preparation checklist, the
  read-only queries and catalogs, and builds installation media
- **Builds on:** `2026-10-27-core-and-drivers-design.md` (run engine, reboot and
  resume, elevation, sub-project table, "the WPF app (`FormatKit.App`) arrives
  in sub-project 7 and reuses the same core"),
  `2026-10-29-preparation-checklist-design.md` (`prepare check`),
  `2026-10-29-installation-media-design.md` (`media build`, `media usb`, on
  branch `feature/installation-media`, PR jpst-dev/formatkit#1) and the
  cleanup, programs and dev environment specs (what the plan and catalogs show)

## 1. Purpose

Everything FormatKit does today needs a terminal. The UI removes that: a person
who has never seen the project can check that a PC is safe to format, make the
installation stick, and run their profile after the reinstall, from one window.

The first user is the author, who wants a more comfortable way to do what the
CLI does. Later the app is shared with friends who are technical but do not
know FormatKit. So every screen explains itself in plain words; it is not a
wizard for complete beginners, but nothing assumes the reader knows the CLI.

The long-term goal is **parity**: everything the CLI does can be done from the
UI. Version 1 (this spec) covers the reinstall cycle; the rest is listed in
section 2 as later versions.

### Success criteria

- In the test VM, the example profile runs end to end from the UI, including a
  reboot that resumes in the UI at the next logon, with no terminal opened by
  the user.
- The checklist, the hardware and driver scan, the cleanup diagnosis and the
  catalogs work without any UAC prompt.
- One UAC prompt per run (none for a resumed run started by the scheduled task).
- The CLI's behavior does not change: its existing tests pass unchanged, except
  for moves of code they reach through `InternalsVisibleTo`.
- A disk is never erased without the same checks and typed confirmation that
  `media usb` requires.

## 2. Scope

**In version 1:**

- A shared library `FormatKit.Hosting` that holds the composition root and the
  flows the CLI and the UI both need (section 4.1). The CLI moves onto it.
- A WPF app `FormatKit.App`, published as `FormatKitUI.exe` (section 4.2), with
  the screens in section 5: Home, Checklist, Run profile, Hardware, Cleanup
  diagnosis, Catalogs, Installation media, About.
- An elevated worker mode of the same exe, talking to the window over a named
  pipe (section 6).
- Resume after reboot in the UI (section 6.4).
- A new-console `IInteractiveConsole` for the WSL password and SSH passphrase
  when the run has no console (section 6.5).
- The installation media screen (section 5.7) as the **last phase**, built only
  after PR jpst-dev/formatkit#1 is merged.

**Later versions (out of scope here):**

- **Version 2 — actions:** `cleanup undo`, `drivers backup-network`,
  `programs export`, `programs search`, `dev wsl-user`, `dev ssh-key`. Until
  then they stay CLI commands; the UI's About page lists them with a one-line
  description so a user knows they exist.
- **Version 3 — profile editor:** creating and editing profiles through forms
  (catalog checkboxes, cleanup level, dev fields) with live validation. Version
  1 only opens and shows a profile; it is edited in a text editor and reloaded.
- A language selector (D5).

**Never:** running a profile that differs from the one the user reviewed (D7);
showing or logging passwords and passphrases.

## 3. Decisions

| # | Decision | Why |
|---|---|---|
| D1 | Scope B + media: run flow, checklist, queries and catalogs, then media as the last phase. | Covers "before formatting → stick → after formatting"; actions are rare or risky and come in v2. Asked and chosen by the user. |
| D2 | Profiles are opened and shown, not edited; the editor is v3. | An editor over four modules and their validation rules is a sub-project on its own. Chosen by the user. |
| D3 | The UI runs unelevated; an elevated worker (the same exe with `--worker`) runs the engine. | Privilege only when needed, as the CLI does; queries and checklist never prompt. Chosen by the user. |
| D4 | Two independent self-contained single-file exes: `formatkit.exe` (CLI) and `FormatKitUI.exe` (UI). | Each works alone; one exe for both would need `AttachConsole` hacks. Chosen by the user. The UI exe is not called `FormatKit.exe` because Windows file names are case-insensitive: it would collide with `formatkit.exe` in `artifacts/`, a Downloads folder and the release assets. |
| D5 | Language follows the Windows UI language (en, pt-BR), like the CLI. | Consistent; reuses the resources; a selector can come later. Chosen by the user. |
| D6 | A run resumes in the app that started it: a UI run registers `FormatKitUI.exe --resume`. | One window from start to finish. Chosen by the user. |
| D7 | The worker refuses to run when the profile's hash differs from the one the UI reviewed. | The user must never run something other than what they saw. |
| D8 | The UI uses the core in process (approach 1), not the CLI's output. | Reuses all the code, keeps each exe independent; the only new infrastructure is the progress pipe. Chosen by the user over driving `formatkit.exe --json`. |
| D9 | WPF's built-in Fluent theme (`ThemeMode="System"`), no third-party UI library. | Native Windows 11 look, follows light and dark mode, no extra dependency. |
| D10 | MVVM with `CommunityToolkit.Mvvm`; ViewModels are tested, views are not. | ViewModels hold the logic and run with the existing fakes; UI automation would be slow and brittle. |

## 4. Architecture

```
src/FormatKit.Hosting      composition root and shared flows (new)
src/FormatKit.Cli          System.CommandLine commands over Hosting
src/FormatKit.App          WPF app + worker mode, FormatKitUI.exe (new)
tests/FormatKit.Hosting.Tests
tests/FormatKit.App.Tests  ViewModels, pipe protocol, worker
```

`FormatKit.App` references `Hosting`, which references Core and the module
projects (Drivers, Cleanup, Programs, Dev, Prepare and, in the media phase,
Media). The CLI references `Hosting` instead of building things itself.

### 4.1 `FormatKit.Hosting`

Moves out of `FormatKit.Cli`, without changing behavior:

- `HostEnvironment` (today's `CliEnvironment` without `TextWriter Out`) and
  `Services.CreateReal`. `CreateReal` takes the resume arguments and the
  rebooter and interactive console to use, so the CLI and the worker each pass
  their own (sections 6.3-6.5). The CLI keeps a thin `CliEnvironment` that adds
  `Out`.
- `ModuleSet`: the module list now built in the `CliApp` constructor, with the
  catalogs (`VendorCatalog`, `CleanupCatalog`, `KnownIssues`,
  `ProgramCatalog`).
- `ProfileService.Load(path)`: `ProfileLoader` plus the built-in drivers-only
  profile, returning the loaded profile or the error list (path and message).
  The CLI prints the errors as today; the UI lists them.
- `PlanService.Build(profile)`: `PlanBuilder` plus `SimulatedRebootStep`.
- `PrepareRunner.RunAsync(profile?, ct)`: what `PrepareCommands` does before
  rendering (environment, volume list, inputs, checklist), returning the
  `PrepareReport` and the `WindowsVolume`; `WriteMarkdown(path, ...)` returns
  whether the path is on the Windows drive and any write error.
- `SystemQuery.GuardAsync`: today's `Output.GuardSystemQueryAsync` without the
  console, returning a result or a localized error message.
- `RunSession`: what `RunFlow.ExecuteAsync` does after the dry run and the
  elevation check: build the engine with a given `IRunProgress`, run it,
  remove the resume task when a resume ends early, return the `RunSummary`.
  The CLI keeps the printing and the exit code mapping.

`SchtasksScheduledTasks` takes the resume arguments as a constructor parameter
instead of the fixed `run --resume --wait-on-exit`; the CLI passes those,
the UI passes `--resume`.

Strings that both use stay in their module's resources. Strings for UI screens
live in `FormatKit.App/Resources/Strings.resx` and `Strings.pt-BR.resx`; when a
CLI string moves with code into Hosting, its entries move to a Hosting resource
file in both languages.

### 4.2 `FormatKit.App`

- `OutputType` `WinExe`, `AssemblyName` `FormatKitUI`, `UseWPF`, `win-x64`,
  `PublishSingleFile`, self-contained on publish, `SatelliteResourceLanguages`
  `en;pt-BR`, embedded debug symbols: the same publish settings as the CLI.
- `ThemeMode` is experimental in WPF (diagnostic `WPF0001`). The project
  suppresses that one diagnostic with `<NoWarn>`, since
  `TreatWarningsAsErrors` would otherwise break the build; nothing else is
  suppressed.
- `App.xaml.cs` reads the arguments: none → the window; `--resume` → the window
  opening on Run profile in resume mode; `--worker --pipe <name> ...` → the
  worker, with no window (section 6).
- A named mutex `Local\FormatKit.App` keeps one window per user session; a
  second start brings the first window to the front and exits. A second start
  with `--resume` (the resume task finding a window already open) signals the
  event `Local\FormatKit.App.Resume` instead, and the open window comes to the
  front, opens Run profile and resumes the run. The mutex and both events grant
  only the user. The worker does not take the mutex.
- Views and ViewModels per page, one `MainViewModel` with the navigation, and a
  `IDialogService` (file pickers, confirmations, reboot countdown) so
  ViewModels never touch WPF types and are tested with a fake.

## 5. Screens

One window with a left navigation menu. Every page that queries the system
shows a progress indicator while it works, cancels the query when the user
leaves the page, and shows errors inside the page (section 7).

### 5.1 Home

Two sentences on what FormatKit does, then two paths: **Before formatting**
(Checklist, Installation media) and **After formatting** (Run profile). A
banner says the tool changes the system and can reboot, and recommends trying
it in a virtual machine first. When `RunStateStore` holds an interrupted run, a
second banner says so with a **Resume** button (section 7).

### 5.2 Checklist (`prepare check`)

- An optional profile (file picker) and a **Check** button.
- Results grouped by status (Blocker, Attention, Not checked, Ok) and then by
  group, with an icon and color per status, each item with what was found and
  the action. Ok items start collapsed.
- The summary line from the CLI (`N items block formatting.` and so on).
- **Save as Markdown**: a save dialog, the same Windows-drive warning as
  `--output`, and the written path or the error. On a failed save the page
  shows only the failure, without the "Windows drive will be erased" warning.
- An invalid profile shows its error list instead of results.

### 5.3 Run profile

1. **Choose:** open a `.json` profile, or the built-in "Drivers only" profile
   (the one `drivers install` uses). The last profile path is remembered in
   `%LOCALAPPDATA%\FormatKit\app.json`; a missing file is simply not offered.
2. **Review:** an invalid profile shows its errors (JSON path and message). A
   valid one shows the plan as the dry run does: steps in order with an "admin"
   badge, the cleanup items and the programs inside their steps. **Reload**
   reads the file again.
3. **Run:** a confirmation dialog ("This changes the system and can restart the
   computer"), then the UAC prompt when the plan needs it (a plan with no admin
   step runs the worker unelevated).
4. **Progress:** every step with its state (pending, running, succeeded,
   skipped, failed, reboot required), the current step highlighted, and a
   **Stop** button that stops after the current step (like Ctrl+C). A stopped
   run ends as `failed` with the worker's Worker_Stopped text ("Stopped after
   the current step. Use Resume to continue the run.") and the window offers
   **Resume**; any other failure after a stop shows its own message.
5. **Result:** each step's messages and next action, the final status in plain
   words (the same texts as the CLI's run summary), and **Open report folder**.
6. **Reboot:** a dialog with a 60-second countdown, **Restart now** and
   **Cancel**. Cancel leaves the run to resume at the next logon, as in the CLI.
   When the reboot does not happen (cancelled, or `shutdown.exe` failed), the
   result shows the CLI's reboot-pending text, or its "resume not scheduled"
   text when `RunSummary.ResumeScheduleFailed` is set.

### 5.4 Hardware

Cards for machine, board, BIOS, GPUs (with driver version), network adapters
and problem devices, from `IHardwareInfo`. A **Scan for drivers** button runs
the driver scan and shows its result on the same page (machine type, online,
available updates, GPU packages, Windows Update error).

### 5.5 Cleanup diagnosis (`cleanup check`)

The `DiagnosisReport` grouped as the CLI groups it ("Breaks Windows features",
"Security risks", and the rest), each finding with its explanation and fix. The page also shows the NotChecked
items and the journal error, so it never gives a false all-clear.

### 5.6 Catalogs

Two tabs, **Cleanup** and **Programs**, read-only. Filters by level and
category (cleanup) or category (programs), and a text search over id and
title. Selecting an item shows its details: what it does, what it breaks, the
gain and the markers (contested, laptop) for cleanup; the description, winget
id, source and context for programs.

### 5.7 Installation media (last phase)

Designed now from `2026-10-29-installation-media-design.md`; at the start of the
phase it is checked against the merged code of PR jpst-dev/formatkit#1 and this
section is corrected where they differ.

- **Inputs:** the Windows 11 ISO (file picker), an optional profile (without
  one the stick is a plain Windows 11 stick) and the target: **Folder**
  (`media build`) or **USB stick** (`media usb`), with **Allow an external hard
  disk** for `--allow-hard-disk`.
- The profile's `setup` section is shown read-only (account, edition, computer
  name, language, keyboard, time zone), with the default that applies to each
  missing field.
- Mounting the ISO and listing disks need administrator rights, so **Continue**
  starts the worker (section 6) with a media job: one UAC prompt.
- **USB stick:**
  1. The worker validates the ISO, the profile and its inputs, and sends the
     disks: eligible ones with number, model, size, bus, letters and labels;
     ineligible ones with the reason (for example "holds the profile; copy it
     elsewhere first").
  2. The user selects a disk and **types its number** in a text box. The text
     says EVERYTHING on it will be erased, and adds the external hard disk
     warning when it applies. Anything other than the number cancels.
  3. The worker lists the disks again and re-checks rules 1-5 for the disk with
     the same `UniqueId` and number; any difference stops without erasing. Then
     it erases and writes, sending progress.
- **Folder:** the same without the disk choice; the folder is picked in a
  dialog and must not exist or be empty.
- **Result:** "The USB stick is ready. Boot the PC to format from it." or the
  folder path, and the reminder to run the Checklist on that PC first. A
  failure or stop after the erase says the stick is incomplete and that
  running again fixes it.

The rules, the confirmation check and the re-check stay in `FormatKit.Media`;
the worker only replaces console input and output with pipe messages. If the
media code reads the typed number from the console, the phase adds a seam for
it (an interface for the confirmation) with tests, without changing the CLI's
behavior.

### 5.8 About

Version, repository link, **Open log folder**, and the CLI commands that the UI
does not cover yet (section 2) with one line each.

## 6. Worker and pipe

### 6.1 Start

The window creates a pipe name `FormatKit-<guid>` and starts
`FormatKitUI.exe --worker --pipe <name> --client-pid <pid> <job>` where the job is
`run --profile <path> --profile-hash <hash>`, `run --resume` or, in the media
phase, `media ...`. It uses `runas` when the job needs administrator rights and
the window is not elevated, and a plain start otherwise (a resumed window is
already elevated, so no UAC). A declined UAC prompt shows a message on the page
and returns to the review; nothing has changed.

The worker creates the pipe server, with an ACL that allows only the current
user, accepts **one** connection and exits with an error if none arrives within
30 seconds. The window connects as the client. The worker checks the pipe
client's process id against `--client-pid` and exits with 2 on a mismatch.

### 6.2 Protocol

One JSON object per line, UTF-8, each with a `type`. The DTOs are records in
`FormatKit.App/Worker/Messages`, serialized with `System.Text.Json` source
generation.

Worker → window:

| Type | Fields |
|---|---|
| `planned` | steps: id, title, requiresAdmin, doneBefore; profileName (optional: missing keeps the name shown) |
| `stepStarted` | number, total, id |
| `stepFinished` | id, outcome, messages, nextAction |
| `rebootCountdown` | seconds |
| `finished` | the `RunSummary` (status, step reports, report directory, error) |
| `failed` | a localized message (worker error before or outside the engine) and a reason: `other`, `stopped` or `profileChanged` (optional: missing is `other`); the window acts on the reason, never on the text |
| `consoleOpened`, `consoleClosed` | a step opened, then closed, a console window for the user to type in (section 6.5) |
| `consoleLine` | text: a line the step writes for the user (public data only) |
| `disks`, `mediaProgress`, `mediaFinished` | media phase only |

Window → worker:

| Type | Meaning |
|---|---|
| `cancel` | stop after the current step |
| `rebootNow` / `rebootCancel` | answer to `rebootCountdown` |
| `confirmDisk` | media phase: the typed disk number and the selected disk's `UniqueId` |

Unknown types are logged and ignored, so a mismatch cannot crash either side.
Messages carry only what the CLI already prints: never a password, passphrase,
product key or remote URL.

### 6.3 Running

The worker loads the profile through `ProfileService`, compares its hash with
`--profile-hash` (D7: a difference sends `failed` with "The profile changed
since you reviewed it. Review it again." and exits), builds the plan through
`PlanService`, sends `planned`, and runs `RunSession` with a `PipeRunProgress`
that sends `stepStarted` and `stepFinished`. Pipe messages from the window are
read on a background loop; `cancel` cancels the run's token.

### 6.4 Reboot and resume

The worker's `IRebooter` is a `PipeRebooter`: it sends `rebootCountdown` and
waits for `rebootNow`, `rebootCancel` or the countdown's end, then runs
`shutdown.exe /r /t 0` as `ConsoleRebooter` does (the shutdown part is shared,
not copied). The window shows the countdown dialog (section 5.3).

A run started from the UI registers the resume task with `FormatKitUI.exe
--resume` (D6). At the next logon the task starts the window elevated; the
window opens on Run profile and starts the worker with `run --resume` without
`runas`. When a window is already open, the task's start signals it to resume
instead (section 4); that window uses UAC when it is not elevated. The `planned`
message carries the profile name, so the header shows it. Resume errors
(nothing to resume, corrupt state, profile changed or invalid) are shown with
the CLI's texts, and the task is removed as the CLI removes it.

### 6.5 Passwords and passphrases

The worker has no console, so it gets a `NewConsoleInteractiveConsole`: it runs
the attached process (`passwd`, `ssh-keygen`, `ssh-add`, the dotfiles `git
clone` that may prompt) in a **new console window** with the same arguments,
environment and timeout handling as `WindowsInteractiveConsole`, and waits for
it. `CanPrompt` is true when a user is signed in at the console. `WriteLine`
goes to the run log and, as a step message (`consoleLine`), to the window.
The window shows "If the console window that opened asks for something, type
it there." on the running step between `consoleOpened` and `consoleClosed`
(every attached console sends them, also the dotfiles clone that normally asks
nothing). The process arguments are not
sent. The values typed never pass
through FormatKit, as today.

## 7. Error handling

- **UAC declined:** message on the page, back to the review (section 6.1).
- **Profile changed between review and run:** the worker refuses (D7).
- **Closing the window during a run:** a dialog asks "Stop after the current
  step?"; on yes the window sends `cancel`, waits for `finished`, then closes.
  On no it stays open. A pending start (the UAC prompt, the worker's
  connection) counts as a run: on yes the start is cancelled, and a worker that
  connects anyway is sent `cancel` and waited for.
- **Pipe broken or window gone:** the worker treats it as `cancel`: it finishes
  the current step and stops; the run state stays saved for **Resume**.
- **Worker exits unexpectedly** (pipe closed without `finished`): the page says
  the run ended unexpectedly, with **Open log folder** (built) and **Open
  report folder**; **Resume** is offered when `RunStateStore` holds a run.
- **Interrupted run found at start:** Home shows the Resume banner (section
  5.1). Resume starts the worker with `run --resume` (with UAC when the window
  is not elevated).
- **Query errors** (WMI, COM, access): the page shows the localized message
  from `SystemQuery.GuardAsync` and logs the exception. The window never closes
  on an error; an unhandled exception is logged and shown in a dialog.
- **Logs:** the window and the worker each log through `LogSetup` to their own
  `run-<stamp>-<pid>.log` file in the usual logs folder (one file per process,
  as today), without the console sink.

## 8. Testing

- **Hosting:** unit tests for every moved flow, with the fakes in
  `tests/FormatKit.Core.Tests/Fakes`. The CLI's existing tests must pass after
  the move without changes to what they assert.
- **App ViewModels:** each page's ViewModel with fake services and a fake
  `IDialogService`: loading a valid and an invalid profile, the plan shown,
  the run states from a scripted sequence of worker messages, stop, the reboot
  dialog answers, the checklist grouping and summary, catalog filters and
  search, query errors shown in the page.
- **Protocol:** serialization round trips of every message; unknown types
  ignored; a partial line or a closed pipe ends the reader cleanly.
- **Worker:** with an in-memory stream pair instead of a named pipe: a fake
  plan runs and sends the expected messages; `cancel` stops after the current
  step; a changed hash refuses; a broken stream cancels; `PipeRebooter`
  answers.
- **Not automated:** XAML layout, UAC, the real named pipe ACL and the new
  console window; they are covered by the VM scenarios.

### 8.1 VM scenarios

Run in the Hyper-V test VM (`tools/vm/vm.ps1`) with the published
`FormatKitUI.exe`, on the signed-in user's desktop (`vm.ps1 job -Visible`):

1. Open the app; Home, Hardware, Scan for drivers, Cleanup diagnosis, Catalogs
   and Checklist work with no UAC prompt.
2. Open an invalid profile: the errors are listed. Open the example profile:
   the plan matches `formatkit run --dry-run`.
3. Run a profile with a simulated reboot (`FORMATKIT_TEST_REBOOT_AFTER`, read by `Services.CreateReal`, so it
   also works in the worker):
   one UAC prompt, progress, the countdown dialog, reboot, the window opens by
   itself at logon, the run finishes, the result and report folder are shown.
4. Stop during a run; then close the window during a run.
5. A dev profile with `wsl.user`: the new console window opens for `passwd`
   (the user types the password in the VM console).
6. Edit the profile file after the review, then click Run: the worker refuses.
7. Media phase: build a folder; list disks with a virtual USB disk if the VM
   can present one, otherwise the real stick on the host as in media scenario
   27, run by the user.

## 9. Phases

The plan follows these phases, each ending with its tests green:

1. `FormatKit.Hosting`: move the composition root and flows; the CLI on top;
   no behavior change.
2. `FormatKit.App` skeleton: project, theme, navigation, mutex, About, Home.
3. Read-only pages: Hardware, Cleanup diagnosis, Catalogs, Checklist.
4. Worker and pipe: protocol, `PipeRunProgress`, `PipeRebooter`,
   `NewConsoleInteractiveConsole`, resume arguments.
5. Run profile page: choose, review, run, progress, result, reboot, resume,
   interrupted-run banner.
6. Publish, README and VM scenarios 1-6.
7. Installation media page, after PR jpst-dev/formatkit#1 is merged into
   `main` and this branch is brought up to date; VM scenario 7.

## 10. Documentation

- README: the UI in Status, a "Graphical interface" section (download, what
  each page does, that it asks for UAC only when running), the build and
  publish commands for `FormatKit.App`, and the Portuguese summary.
- `CLAUDE.md`: the new projects in Layout and the App publish command.
- This spec's "Found while running" section after the VM scenarios.

## 11. Review focus

- The CLI's behavior is unchanged after the Hosting move.
- The worker never runs a profile the user did not review (D7), and never sends
  secrets over the pipe.
- The pipe accepts only the current user and one connection.
- Cancel, broken pipe and window close always stop after the current step,
  never in the middle of an installer.
- The media page cannot erase a disk without the typed number and the re-check.

## Decided in away mode

The user approved D1-D6, D8 and sections 1-3 of the design in conversation and
then switched to away mode. These were decided by Claude's recommendation and
await review:

- **Authority:** this spec is the authority. The lines below align it with what
  was built and ruled during implementation.
- **Exe name `FormatKitUI.exe` (D4):** case-insensitive file names made
  `FormatKit.exe` collide with `formatkit.exe`; ruled by the controller.
- **Catalogs developer category:** follows `cleanup list --level`.
- **Copied CLI result texts:** stay word for word even where they name
  `formatkit run ...`.
- **Reboot countdown:** completes and restarts if the window disappears.
- **Section 4 of the conversation (error handling, section 7 here):** presented
  and not yet approved when the user left; written as presented.
- **D7 (profile hash check):** recommended in the error handling section.
- **D9 (Fluent theme) and D10 (MVVM toolkit, ViewModels tested):** stated as
  assumptions in the understanding note, which the user approved; written as
  decisions.
- **Built-in "Drivers only" profile on Run profile:** included because it is
  the same run path as `drivers install`, at no extra cost.
- **Remembered last profile in `%LOCALAPPDATA%\FormatKit\app.json`:** a small
  comfort for the author's repeated use; no other settings are stored.
- **About page lists the CLI-only commands:** so v1 users know they exist
  without reading the README.
- **A plan with no admin step runs the worker unelevated:** mirrors the CLI,
  which elevates only when a step needs it.
- **Testing approach and phases (sections 8 and 9):** not presented in
  conversation; written from the recommended design.

## Found while running

Fixed before the remaining VM scenarios. I1-I5 come from the final review;
V1-V3 from VM scenario 1 and V4 from VM scenario 2 (2026-10-29, Brazilian
Portuguese UI, 1024x768).

- **I1 (single instance):** the elevated resume window created the mutex and
  activation event with an admin-only DACL; both now grant only the user, and
  access denied counts as another instance.
- **I2 (startup failure):** a startup or XAML failure left a hidden process
  holding the mutex; it now logs, shows the error and exits with 1.
- **I3 (frozen window):** page queries ran on the UI thread; they now run on
  the thread pool and apply on the UI thread.
- **I4 (console hint):** the running step now says "Type in the console
  window that opened" and shows the step's lines (`consoleOpened`,
  `consoleLine`, `consoleClosed`, section 6.2).
- **I5 (Resume):** the result page now has the Resume button its stop text
  names.
- **V1 (accessibility):** list items were read as their record's `ToString`;
  every list over records names its items, and the About link has a name.
- **V2 (Home text):** "prepare the installation media" removed until the
  media phase.
- **V3 (window size):** the window opened cut off on 1024x768; it now opens
  centered, clamped to the work area.
- **V4 (admin badge):** the plan badge read "admin"; it now uses the CLI's
  dry-run word ("administrator" / "administrador").
- **V5 (resume rows):** on `run --resume` the rows of steps done before the stop or reboot stayed "Pending"; the `planned` message now carries each such step's earlier outcome (`doneBefore`) and the window shows it with "Done before the stop or reboot." (VM scenario 4, 2026-10-29).
- **V6 (profile changed):** the worker's hash refusal landed on the result page with every row Pending and a Resume offer; it now returns to the review with the message so the user can Reload (VM scenario 6, 2026-10-29).

VM scenarios (section 8.1), 2026-10-29, Windows 11 build 26300 Hyper-V VM with
nested virtualization, Brazilian Portuguese UI, 1024x768, driven through UI
Automation and the Hyper-V synthetic keyboard:

- **1 (pages, no UAC): passed.** Home, Hardware, Scan for drivers, Cleanup
  diagnosis (same result as `cleanup check`), Catalogs, Checklist and About
  show real data with no UAC prompt. Found V1-V3.
- **2 (open a profile): passed.** An invalid profile lists each error with its
  JSON path and message; the example profile's plan has the same ids, titles
  and order as `run --dry-run`. Found V4.
- **3 (run, reboot, resume): passed.** One UAC prompt for `FormatKitUI.exe`,
  progress, the countdown dialog, the resume task `"FormatKitUI.exe" --resume`,
  the reboot, the window opening by itself at logon, the run finishing with no
  UAC prompt, the result and the report folder; the resume task is removed at
  the end. With the elevated resume window open, an unelevated second start
  exits quietly (I1).
- **4 (stop, close): passed.** Stop waits for the current step and offers
  Resume (I5); closing during a resumed run asks "Stop after the current
  step?", waits for the step and closes with no process left. Found V5.
- **5 (WSL user): passed.** The WSL platform install rebooted the VM, the
  window resumed by itself, installed Ubuntu and opened a `wsl.exe` console
  for `passwd`, where the user typed the password; the run finished with
  `user fkuser: done` and `passwd -S` reports `P`. The console covered the
  window, so the "Type in the console window that opened" hint (I4) was not
  seen on screen; unit tests cover it.
- **6 (profile changed): passed.** The worker refuses a profile edited after
  the review. Found V6.
- V5 and V6 were checked again on the fixed build: a step done before a
  simulated reboot shows its earlier outcome after the resume, and the hash
  refusal returns to the review with Reload.
- **7 (media):** not run; waits for the media phase.

Polish after merge, 2026-10-29 (unit-tested; P1 and P3 also run in the VM, the others only unit-tested):

- **P1 (resume with a window open):** a `--resume` start that found a window open only brought it forward; it now signals `Local\FormatKit.App.Resume` and that window opens Run profile and resumes (section 4). In the VM the window, left on Hardware, went back to Run profile, asked for UAC and finished the run. The window also starts listening only after it subscribes, so a request sent while it is still starting waits on the event.
- **P2 (close during the start):** closing during the UAC prompt or the worker's connection closed at once; it now asks "Stop after the current step?" and on yes cancels the start (section 7).
- **P3 (resumed profile name):** a resumed run showed no profile name; `planned` now carries `profileName` (section 6.2); the VM showed "Reboot test" after a real reboot and after a resume in the open window.
- **P4 (console hint):** the hint showed for consoles that ask nothing; it now reads "If the console window that opened asks for something, type it there." (section 6.5).
- **P5 (refusal by text):** the window recognized the profile-changed refusal by its localized text; `failed` now carries a `reason` code (section 6.2).
