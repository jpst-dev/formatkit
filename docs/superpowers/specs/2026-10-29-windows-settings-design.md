# FormatKit — Windows Settings and Optimization Design

Sub-project 1 of the "improvements" round. Extends the cleanup catalog and
engine (`docs/superpowers/specs/2026-10-27-cleanup-design.md`, "the cleanup
spec") so one profile also sets up the Windows settings the author redoes by
hand after every reformat, and adds the optimizations backed by the research
report `reports/Serviços e apps do Windows 11.md` (outside the repo, kept in
the author's workspace; its conclusions are summarized in section 3).

## 1. Purpose

After a reformat the author spends an hour clicking through Settings: taskbar
and Explorer, AI features and ads, dark theme and power, keyboard (pt-BR
language with the US-International layout), sound. FormatKit should do all of
it from the profile, with the same guarantees as cleanup: every change is
checked, journaled, verified and undoable, and every item says what it does,
what it breaks and what it gains.

The second goal is honest optimization: keep what measurably helps (stopping
processes from starting at all) and offer what people pay "PC optimizers" for
(a tuned power plan) clearly marked as contested.

## 2. Scope

In scope:

- New catalog categories `startup`, `interface`, `ai`, `appearance`, `input`,
  `sound`, `power`, plus new items in the existing `apps`, `privacy` and
  `performance` categories (section 7).
- Engine additions: `optIn` items, `restartsExplorer`, a `noPrinter`
  condition, registry `binary` kind and default values, three new action
  types (`scheduledTask`, `startupItem`, `powerSetting`), a `formatkit` power
  scheme, three named handlers (section 5).
- `cleanup check`: a "reverted by Windows" group (section 9).
- CLI, WPF app and site pick the new categories up from the catalog.

Out of scope (decisions D7, D8):

- Default apps (browser, player, PDF). Windows 11 protects the per-user choice
  (UserChoice hash, UCPD driver); programs cannot set it. Covered by the guide
  on the site (sub-project 3). Writing default associations for new users into
  the installation media (`DISM /Import-DefaultAppAssociations`) is a follow-up
  for the installation media sub-project.
- Capturing a machine into a profile (sub-project 2).
- Value parameters in the profile (D2).

## 3. What the research changes

The VM measurements (one clean Windows 11 Pro VM, cumulative groups, one
sample each, idle cost only) showed:

- Startup items and resident apps are where the idle cost is: OneDrive at
  logon (~86 MB), Widgets (~90 MB with WidgetService) and their WebView2
  processes. Baseline to all groups: 135 → 117 processes, 99 → 88 running
  services, about −530 MB in use (part of it updates finishing).
- The Widgets policy alone hides the board but `Widgets.exe` stays resident;
  only removing `MicrosoftWindows.Client.WebExperience` frees it.
- Changing service start types saved no RAM. Most classic targets (Bluetooth,
  Xbox, PhoneSvc, SSDPSRV, StiSvc, SCardSvr) already ship Manual and stopped;
  changing them gains nothing and is the main source of breakage reports.
  Disabling SysMain showed no idle gain.
- Telemetry tasks and policies cost little at idle; they stay for privacy.
- Core parking, EPP, ASPM, HPET, timer resolution, Nagle and similar
  "latency" tweaks have no published measurement of a gain.
- Windows updates revert interface settings and reinstall removed apps, so
  re-checking matters more than writing once.

## 4. Decisions

| # | Decision | Reason |
|---|---|---|
| D1 | Extend the cleanup catalog and engine (approach A) instead of a separate settings module. | Reuses journal, undo, check, levels, contested flag, CLI, UI and the site's catalog pages. |
| D2 | No value parameters in the profile. Where a choice exists, the catalog offers variants as separate items (for example two monitor timeouts). | Keeps the profile format, its validation and the UI unchanged; add parameters only when variants stop scaling. |
| D3 | Personal preferences (theme, keyboard layout, sound scheme, timeouts) are `optIn`: never selected by a level, only by `include`. This generalizes the rule developer items already follow. | Levels mean "how far to clean", not "what I like". |
| D4 | Service items only for services that run at idle on a clean install, and none for services that ship Manual and stopped. | Measured: no gain there, only breakage. |
| D5 | The FormatKit power plan is offered, marked `contested`, `aggressive`, `optIn` and `laptopSensitive`. | The author asked for it and uses an equivalent plan; the research found no measured gain, so it is labeled honestly. |
| D6 | Items that restart Explorer instead of requiring a reboot declare `restartsExplorer`; the step restarts Explorer once at its end. | Taskbar and Explorer values apply only after Explorer restarts. |
| D7 | Default apps are not an item. | Not settable by programs on Windows 11 (UCPD). |
| D8 | Existing item ids never change category, even when a new category fits better (`performance.pointer-precision` stays). | Ids live in profiles and journals. |
| D9 | Exact registry locations and values are verified against Microsoft documentation and in the VM during implementation; an item that cannot be verified is dropped, not guessed (same rule as the cleanup spec). | Interface keys change between builds. |

## 5. Engine changes

### 5.1 Item fields

| Field | Default | Meaning |
|---|---|---|
| `optIn` | false | Selected only by `include`. It still carries a `level`, which drives `confirmAggressive` and the level badge in the UI; an included `laptopSensitive` item applies on a battery, as today. `developer` items are `optIn` implicitly (the validator requires it to be true or absent for them). |
| `restartsExplorer` | false | After the step applies such an item, Explorer is restarted once at the end of the step (`taskkill /f /im explorer.exe`; if Winlogon has not restarted it within 3 s, `cmd /c start explorer.exe` in the console user's session through the limited-token user-context runner, never from the elevated process; section 12). Not a reboot. |
| `condition` | absent | `noPrinter`: the item is skipped ("not applicable: a printer is installed") when any printer other than the built-in virtual ones (Microsoft Print to PDF, Microsoft XPS Document Writer, OneNote, Fax) is present. |
| `laptopSensitive` | false | Now allowed on items with a `powerScheme` or `powerSetting` action, or the `hibernate` handler. |

Validation (cleanup spec 5.2) gains: the id pattern accepts the new
categories; `contested` still requires `aggressive`; `condition` must be a
known value; `scope: user` items may use HKCU registry, `startupItem` with
scope user, and the user-scope named handlers (`keyboardLayout`,
`soundScheme`).

### 5.2 Registry action

- Kind `binary`: `value` is a lowercase hex string (`"0300000000000000"`),
  compared byte for byte.
- `name: ""` addresses the key's default value (needed for the classic
  context menu item). Absent default value is journaled as "absent" and
  deleted on undo, like any other value.

### 5.3 New action types

| Type | Fields | Check | Journal | Undo |
|---|---|---|---|---|
| `scheduledTask` | `path` (folder, `\Microsoft\Windows\...\`), `name`, `enabled` (bool) | task state equals target; a missing task is "skipped: not present" | previous enabled state | restore enabled state |
| `startupItem` | `name` (value name under the Run key), `scope` (`user` or `machine`) | the StartupApproved entry under `...\Explorer\StartupApproved\Run` marks it disabled (first byte `03`); a missing Run value is "already in place" | previous StartupApproved bytes or "absent" | restore bytes or delete the StartupApproved value; the Run value itself is never touched |
| `powerSetting` | `subgroup`, `setting` (GUIDs or powercfg aliases), `ac` (int), `dc` (int, optional) | the active scheme's AC/DC indexes equal target | scheme GUID and previous AC/DC indexes | restore the indexes on that scheme if it still exists |

`powerSetting` writes the scheme that is active when the item is applied.
Power plan items run before power setting items (catalog order inside the
`performance` and `power` steps, and the `performance` step runs first), so
settings land on the plan FormatKit selected.

Protected scheduled tasks (no `scheduledTask` action may disable them),
added to `ProtectedLists.cs`: everything under `\Microsoft\Windows\WindowsUpdate\`,
`\Microsoft\Windows\UpdateOrchestrator\`, `\Microsoft\Windows\WaaSMedic\`,
`\Microsoft\Windows\Defrag\ScheduledDefrag`, `\Microsoft\Windows\PI\Secure-Boot-Update`,
`\Microsoft\Windows\Autochk\Proxy`, `\Microsoft\XblGameSave\XblGameSaveTask`,
`\Microsoft\Windows\Windows Defender\`, `\Microsoft\Windows\Windows Error Reporting\QueueReporting`.

### 5.4 Power scheme `formatkit`

`powerScheme` accepts a fourth scheme, `formatkit`:

1. If a scheme named "FormatKit Desempenho" / "FormatKit Performance" created
   by FormatKit (journaled GUID) exists, activate it. Otherwise duplicate High
   performance (`8c5e7fda-...`), rename it to the display language's name and
   journal the new GUID as created by FormatKit.
2. On that scheme, AC and DC: core parking off (`CPMINCORES` 100), energy
   performance preference 0 (`PERFEPP`), boost mode aggressive
   (`PERFBOOSTMODE` 2), PCI Express link state power management off
   (`ASPM` 0), USB selective suspend off, hard disk never off. Sleep,
   hibernate, display and wake settings are left as High performance ships
   them.
3. Activate it.

Check: the active scheme is the journaled FormatKit scheme and the six values
match. Undo: re-activate the previous scheme and delete the FormatKit scheme
(existing rule). The plan item joins the existing "only the highest-level
power scheme item is kept" rule; because it is `optIn`, it is only ever
selected by `include`, and when included it wins over the level's plan.

### 5.5 Named handlers

| Handler | Scope | Apply | Check | Journal / undo |
|---|---|---|---|---|
| `keyboardLayout` (item `input.ptbr-us-international`) | user | Through PowerShell `Get-WinUserLanguageList` / `Set-WinUserLanguageList`: make `pt-BR` the first language with exactly the input method `0416:00020409` (US-International), removing ABNT/ABNT2 from pt-BR; other languages keep their order and inputs. | first language is pt-BR and its input methods are exactly `0416:00020409` | previous list serialized (tags and input method tips) / restored with `Set-WinUserLanguageList` |
| `soundScheme` (item `sound.no-sounds`) | user | Set `HKCU\AppEvents\Schemes` default to `.None` and clear every `...\Apps\*\*\.Current` default value. | scheme is `.None` and all `.Current` values are empty | previous scheme name and every `.Current` value / restored |
| `hibernate` (item `power.hibernation-off`) | machine | `powercfg /hibernate off` | `HKLM\SYSTEM\CurrentControlSet\Control\Power\HibernateEnabled` is 0 | previous `HibernateEnabled` (value or absent) and the effective state (`HibernateEnabled`, else `HibernateEnabledDefault`) / when hibernation was effectively on, `powercfg /hibernate on` (a failure is a logged warning, e.g. firmware without hibernation), then `HibernateEnabled` restored or deleted when it was absent |

All three go through the existing process and registry abstractions and are
unit-tested with fakes; PowerShell literals go through
`PowerShellText.SingleQuote`.

## 6. Profile

Unchanged format (cleanup spec 6). `optIn` items are selected only by
`include`; listing one in `exclude` is allowed and has no effect. The
author's profile becomes, for example:

```json
"cleanup": {
  "enabled": true,
  "level": "balanced",
  "include": [
    "appearance.dark-theme", "input.ptbr-us-international",
    "power.monitor-off-15", "power.sleep-never",
    "performance.formatkit-plan"
  ],
  "exclude": [],
  "confirmAggressive": true
}
```

`profiles/example.json` and `profiles/cleanup-balanced.json` gain the new
categories' level items implicitly; a new `profiles/windows-setup-example.json`
shows the opt-in items.

## 7. Catalog content

Levels: S safe, B balanced, A aggressive; "opt" means `optIn`; "c" means
contested. All new user-facing text exists in `en` and `pt-BR`. Values below
are the research's; D9 applies to each.

### 7.1 `startup` (scope as noted)

| Id | Level | Actions |
|---|---|---|
| `startup.onedrive` | S | `startupItem` `OneDrive`, user |
| `startup.edge-startup-boost` | S | HKLM Edge policy `StartupBoostEnabled=0` |
| `startup.edge-background` | S | HKLM Edge policy `BackgroundModeEnabled=0` |

### 7.2 `apps` (new items; allowlist grows accordingly)

| Id | Level | Package |
|---|---|---|
| `apps.widgets` | B | `MicrosoftWindows.Client.WebExperience`, plus HKLM `Dsh\AllowNewsAndInterests=0` |
| `apps.outlook-new` | B | `Microsoft.OutlookForWindows` |
| `apps.dev-home` | B | `Microsoft.Windows.DevHome` |
| `apps.bing-search` | B | `Microsoft.BingSearch` |
| `apps.copilot` | B | `Microsoft.Copilot` |
| `apps.office-hub` | A, c | `Microsoft.MicrosoftOfficeHub` (the Copilot key opens it) |
| `apps.phone-link` (existing) | B | gains `MicrosoftWindows.CrossDevice` |

`Microsoft.ZuneMusic` (Media Player) is not removed: it is the default player.

### 7.3 `interface` (user, `restartsExplorer`)

| Id | Level | Setting |
|---|---|---|
| `interface.taskbar-left` | S, opt | `Explorer\Advanced\TaskbarAl=0` |
| `interface.taskbar-search-icon` | S | `Search\SearchboxTaskbarMode=1` (icon only) |
| `interface.taskbar-task-view-off` | S | `Explorer\Advanced\ShowTaskViewButton=0` |
| `interface.taskbar-end-task` | S | `Explorer\Advanced\TaskbarDeveloperSettings\TaskbarEndTask=1` |
| `interface.explorer-this-pc` | S | `Explorer\Advanced\LaunchTo=1` |
| `interface.classic-context-menu` | B | `HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32` default value `""` |
| `interface.start-recommendations-off` | S | `Explorer\Advanced\Start_IrisRecommendations=0` and `Start_TrackDocs=0` |
| `interface.start-web-results-off` | A, c | `HKCU\Software\Policies\Microsoft\Windows\Explorer\DisableSearchBoxSuggestions=1` (Start search can go blank) |

### 7.4 `ai` (machine unless noted)

| Id | Level | Setting |
|---|---|---|
| `ai.recall-off` | S | `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI\AllowRecallEnablement=0`, `DisableAIDataAnalysis=1` |
| `ai.click-to-do-off` | S | `WindowsAI\DisableClickToDo=1` |
| `ai.paint-off` | S | Paint policies `DisableCocreator`, `DisableGenerativeFill`, `DisableImageCreator` = 1 |
| `ai.notepad-off` | S | Notepad policy `DisableAIFeatures=1` |
| `ai.edge-copilot-off` | S | Edge policy `HubsSidebarEnabled=0` |
| `ai.fabric-service-manual` | A, c | `service WSAIFabricSvc manual` |

### 7.5 `privacy` (new items, user)

| Id | Level | Setting (`ContentDeliveryManager` unless noted) |
|---|---|---|
| `privacy.start-suggestions` | S | `SystemPaneSuggestionsEnabled=0`, `SubscribedContent-338388Enabled=0` |
| `privacy.lock-screen-tips` | S | `RotatingLockScreenOverlayEnabled=0`, `SubscribedContent-338387Enabled=0` |
| `privacy.settings-suggestions` | S | `SubscribedContent-338393Enabled=0`, `-353694Enabled=0`, `-353696Enabled=0` |
| `privacy.tips-notifications` | S | `SubscribedContent-338389Enabled=0` |
| `privacy.silent-app-installs` | S | `SilentInstalledAppsEnabled=0` |
| `privacy.welcome-experience` | S | `SubscribedContent-310093Enabled=0` |
| `privacy.delivery-optimization-no-peers` | S | machine, `DeliveryOptimization\DODownloadMode=0` |
| `privacy.diagtrack-manual` | B | machine, `service DiagTrack manual` |
| `privacy.telemetry-tasks` | B | machine, `scheduledTask` off: `MareBackup`, `StartupAppTask`, `Consolidator`, `UsbCeip`, `Microsoft-Windows-DiskDiagnosticDataCollector`, `DmClient`, `DmClientOnScenarioDownload`, `UsageDataReporting`, `BootstrapUsageDataReporting` (`PcaPatchDbTask` dropped: Windows turns it back on at every boot, section 12) |
| `privacy.compatibility-appraiser` | A, c | machine, `scheduledTask` off: `Microsoft Compatibility Appraiser Exp` (one report of a blocked feature update) |

### 7.6 `performance` (new items, machine)

| Id | Level | Setting |
|---|---|---|
| `performance.formatkit-plan` | A, c, opt, laptopSensitive | `powerScheme formatkit` (5.4) |
| `performance.spooler-off` | B, opt, `noPrinter` | `service Spooler disabled` (breaks Print to PDF) |
| `performance.trkwks-manual` | B | `service TrkWks manual` |

### 7.7 `appearance` (user, all S, opt)

| Id | Setting |
|---|---|
| `appearance.dark-theme` | `Themes\Personalize\AppsUseLightTheme=0`, `SystemUsesLightTheme=0` |
| `appearance.transparency-off` | `Themes\Personalize\EnableTransparency=0` |
| `appearance.animations-off` | `Control Panel\Desktop\WindowMetrics\MinAnimate="0"`, `Explorer\Advanced\TaskbarAnimations=0` |

### 7.8 `input`

| Id | Level | Setting |
|---|---|---|
| `input.ptbr-us-international` | S, opt, user | named `keyboardLayout` |
| `input.sticky-keys-shortcut-off` | S, user | `Control Panel\Accessibility\StickyKeys\Flags="506"` |

### 7.9 `sound` (user)

| Id | Level | Setting |
|---|---|---|
| `sound.communications-no-ducking` | S | `HKCU\Software\Microsoft\Multimedia\Audio\UserDuckingPreference=3` ("do nothing") |
| `sound.no-sounds` | S, opt | named `soundScheme` |

### 7.10 `power` (machine)

| Id | Level | Setting |
|---|---|---|
| `power.fast-startup-off` | S | `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power\HiberbootEnabled=0` |
| `power.hibernation-off` | B, laptopSensitive | named `hibernate` |
| `power.monitor-off-15` | S, opt | `powerSetting` display `VIDEOIDLE` ac 900 |
| `power.monitor-off-30` | S, opt | `powerSetting` display `VIDEOIDLE` ac 1800 |
| `power.sleep-never` | S, opt, laptopSensitive | `powerSetting` sleep `STANDBYIDLE` ac 0 |

`power.monitor-off-15` and `-30` target the same setting; the selector keeps
only the last one in catalog order and logs the drop, like power schemes.

### 7.11 Not included (added to `docs/cleanup-not-included.md`)

Disabling SysMain, WpnService, WSearch, CDPSvc, LanmanServer, iphlpsvc,
DoSvc, WerSvc, any Bluetooth, Xbox or Windows Update service; disabling Game
Bar integration; "background apps" off globally; MPO off; IPv6 off; hosts
file blocking; the `store.db` ACL; `DISM /ResetBase`; turning BitLocker or
reserved storage off. Each with the reason and source from the research.

## 8. Applying

- Steps stay one per category, in this order: apps, startup, privacy, ai,
  interface, appearance, input, sound, performance, power, developer. Step
  ids `cleanup.<category>`; new titles in `Strings.resx` and
  `Strings.pt-BR.resx`.
- A step whose applied items include a `restartsExplorer` item restarts
  Explorer once after its last item; a failure to restart is a warning with
  the next action "sign out and back in".
- Condition and laptop skips are reported like the existing skips (cleanup
  spec 8.3).
- `cleanup list` and the WPF app show `optIn` items with an "opt-in" mark.

## 9. `cleanup check` additions

- New output group **Reverted by Windows**, placed before **FormatKit items
  applied**: journaled, unreverted items whose target no longer matches (an
  update turned the setting back or reinstalled the app). Each line names the
  item and the fix `formatkit run --profile <profile>` (re-applying is
  idempotent).
- Rules for 7.11's not-included changes when detectable (`serviceDisabled`
  for SysMain is informational, not "breaks").

## 10. Error handling

As in the cleanup spec 12, plus:

- `scheduledTask` access denied: item failed with the task path; other items
  continue.
- `startupItem` never deletes the Run value, so a failed undo leaves the item
  only disabled.
- `keyboardLayout` with an unexpected language list (no pt-BR language pack
  installed): it adds pt-BR; if `Set-WinUserLanguageList` fails, the item
  fails and the previous list is restored.
- The Explorer restart is never fatal.

## 11. Testing

- Catalog tests: new categories, `optIn`, `condition`, binary values, default
  value name, protected tasks, allowlist additions, every new item has text in
  both languages.
- Handler tests against fakes for `scheduledTask`, `startupItem`,
  `powerSetting`, `powerScheme formatkit` (create, reuse, undo deletes it),
  and the three named handlers (apply, check, undo, failure rollback).
- Selection: `optIn` only by `include`; monitor timeout variants dedupe;
  FormatKit plan wins over the level's plan when included.
- Step: Explorer restarted once per step, not per item; `noPrinter` skip.
- Check: "Reverted by Windows" from a journal plus a fake state that drifted.
- VM scenarios in `docs/testing.md`:
  1. Clean VM, author's profile (balanced + opt-ins): reboot, verify the
     taskbar, Explorer, dark theme and keyboard on the desktop (screenshot),
     `cleanup check` clean, measure idle cost with the research script and
     compare with G4r.
  2. `cleanup undo --all`, registry export compare as in the cleanup spec,
     keyboard back to ABNT2, the FormatKit plan deleted.
  3. Re-run the same profile: everything "already in place".

## 12. Found while running

VM runs of scenarios 28-30 (`docs/testing.md`) on 2026-10-29/30 (Windows 11
Pro build 26300, Brazilian Portuguese UI, user `fktest` signed in, FormatKit
run through `vm.ps1 job`). An exploratory run on the Task 10 build found the
two bugs below; a full run on the fixed build and a second one without the
measurement passed all three scenarios.

Fixed:

- **Explorer did not come back after the restart.** After `taskkill /f /im
  explorer.exe` Winlogon's AutoRestartShell (on, value 1) did not restart the
  shell: the desktop stayed black, with no taskbar, and the step reported the
  "sign out and back in" warning. The step now waits up to 3 s for Windows to
  restart it, then runs `cmd /c start explorer.exe` through the user-context
  runner (a limited-token scheduled task as the console user), and polls up
  to 15 s more. Checked in the VM: one Explorer, owned by the user and not
  elevated, started during the run, no warning. Explorer started this way
  opens the Start menu once. (`fix(cleanup): start Explorer as the signed-in
  user when it does not restart`.)
- **`PcaPatchDbTask` turns itself back on.** After the reboot of scenario 28,
  `cleanup check` listed `privacy.telemetry-tasks` under "Reverted by
  Windows", and scenario 29 applied the item again. `PcaPatchDbTask` stays
  `Disabled` after `Disable-ScheduledTask` and after a restart of `PcaSvc`,
  and is `Ready` again after every reboot. It was dropped from the item (now
  nine tasks, section 7.5) and described in `docs/cleanup-not-included.md`.
  (`fix(cleanup): stop disabling PcaPatchDbTask in the telemetry tasks item`.)

Behavior worth knowing:

- Scenario 29 on a reverted item: the re-run journals a second entry for the
  same item holding only the reverted action; `undo --all` reverts both,
  newest first, and the compare stayed clean.
- The dark theme reaches the taskbar only after the reboot (or a sign-out):
  the `appearance` step runs after the `interface` step's Explorer restart
  and does not restart Explorer itself. Apps and Start turn dark at once.
  Not changed; a follow-up could broadcast the theme change or move the
  restart to the end of the run.
- `power.monitor-off-15` and `power.sleep-never` report "already in place"
  with `performance.formatkit-plan`: High performance, which the plan copies,
  already ships a 15-minute display timeout and no sleep on AC. They are not
  journaled, which is right: undo deletes the plan.
- `apps.copilot` reports "already in place": `Microsoft.Copilot` is not in
  the build 26300 image.
- The Start menu still showed one "Recommended" entry ("Get Started") right
  after `interface.start-recommendations-off`; the values were written as
  DWORD 0. Not checked after a reboot.
- On a fresh `clean`, OneDrive's first-logon setup was still running in one
  run (`HKCU\...\Run` held `OneDriveSetup`, not `OneDrive`). By the time the
  `startup` step ran, the `OneDrive` value existed and the item applied. If a
  run is faster than OneDrive setup, `startup.onedrive` is "already in place"
  (missing Run value, section 5.3) and OneDrive starts at the next logon; run
  the profile again.
- Undo leaves the empty keys apply created (the policy keys `Dsh`, `Edge`,
  `WindowsAI`, `Paint`, `WindowsNotepad`, `DeliveryOptimization`,
  `AdvertisingInfo`, the HKCU `Explorer` policy key, `Siuf\Rules`,
  `OnlineSpeechPrivacy`, and `StartupApproved\Run`), as the cleanup spec
  records for DeviceGuard. Every catalog value matched the `before` export
  (58 of 59 targets; the 59th, `Run\OneDrive`, was OneDrive setup finishing
  after the export) and the 142 `.Current` lines of the sound scheme were
  identical.
- winget failed two reinstalls once with `-2147012889` (name not resolved);
  `cleanup undo apps.feedback-hub apps.get-help` right after reinstalled both.
  Solitaire fails as in scenario 7.
- An admin could disable `\Microsoft\Windows\WaaSMedic\PerformRemediation`
  (outside FormatKit, as a probe) but not enable it again (access denied):
  the protected task list (section 5.3) is what keeps FormatKit away from such
  tasks, not their ACLs.

Checks the implementation left to the VM (all passed):

- Binary value on the real registry: `startup.onedrive` wrote
  `StartupApproved\Run\OneDrive` as REG_BINARY `030000000000000000000000`;
  undo deleted it (it was absent).
- Default value (`name: ""`): the classic context menu item was dropped in
  Task 10, so the round trip used a journal entry shaped like the one it
  would write (`InprocServer32|`, previous absent) and the value set by hand
  to an empty REG_SZ. `cleanup undo interface.classic-context-menu` reverted
  it and the default value was gone (the key stays). The sound scheme handler
  also writes and restores default values (`HKCU\AppEvents\Schemes` and every
  `.Current`).
- Scheduled tasks, with the exact scripts FormatKit builds: a missing task, a
  missing name in an existing folder and a missing folder all print `Absent`
  (exit 0); the states seen on the machine are `Ready`, `Disabled` and
  `Running`; `Disable-ScheduledTask` on the ACL-protected
  `UpdateOrchestrator\Schedule Scan` exits 1 with "access denied" and the task
  stays `Ready`. Paths and names with spaces (`Application Experience`,
  `Customer Experience Improvement Program`, `Microsoft Compatibility Appraiser
  Exp`) work for check, apply and undo.
- `powercfg /changename` with a name with a space: the plan is listed as
  `FormatKit Desempenho`.
- The keyboard list acts on the desktop user: the elevated job runs as
  `fktest`, and the user's `Get-WinUserLanguageList` shows `pt-BR:
  0416:00020409` after apply and `0416:00010416,0416:00000416` after undo.
- Hibernate: `HibernateEnabled` was absent (`HibernateEnabledDefault` 1),
  apply wrote 0, undo deleted the value. The VM firmware has no hibernation,
  so whether it comes back is not checked.
- REG_EXPAND_SZ: `sound.no-sounds` (applied in a separate run) journaled 22
  `.Current` values as `expandstring`; after undo the 22 are REG_EXPAND_SZ
  again with their `%SystemRoot%` text.
- Ruling M3: after apply, `TaskbarAl` and `ShowTaskViewButton` are REG_DWORD
  0, and the taskbar shows them (left aligned, no Task View).

Idle cost (research `measure.ps1`, 15 min settle, 10 min sample): 125
processes, 98 running services, 2292 MB in use, 2.2 % CPU, against G4r's
117, 88, 2111 MB and 2.5 %. The profile keeps the services G3 changed
(decision D4: Spooler, SysMain, MapsBroker and others), and Windows Update
was still scanning 27 minutes after the restore; Widgets, WidgetService and
OneDrive were not running, as in G4r. Recorded next to G4r in the research
notes (`vm_measurements.md`, row WS1).
