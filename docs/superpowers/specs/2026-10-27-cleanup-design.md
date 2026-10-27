# FormatKit — Cleanup Design

- **Date:** 2026-10-27
- **Status:** Draft, awaiting review
- **Scope:** Sub-project 3 (Cleanup): app removal, privacy and performance tweaks in
  levels, undo, and a read-only diagnosis of already-tweaked PCs
- **Builds on:** `2026-10-27-core-and-drivers-design.md` (profile, step engine,
  resume, logs, report, exit codes)

## 1. Purpose

After a clean install, remove what the user does not want, tighten privacy and
apply performance settings that actually help, with every change explained,
graded by risk and reversible.

The design is shaped by two findings:

- **Market research.** Existing tools (Win11Debloat, WinUtil, privacy.sexy,
  Sophia Script, O&O ShutUp10++) cover the same ground. Their common failure is
  removing system packages and disabling services, which breaks the Store and
  Windows Update, sometimes months later. Most "input lag" registry tweaks have no
  documented gain; HPET tweaks are a myth.
- **The author's own PC**, "optimized" by a paid third party: devices disabled
  (Virtual Drive Enumerator, UMBus, NDIS virtual adapter enumerator, HPET), which
  silently broke ISO/VHDX mounting and Hyper-V; services disabled (W32Time,
  defragsvc, Xbox accessory and cloud-save services, ssh-agent, IKEEXT, SSDP,
  scanner, smart card, sensor and device setup services); Memory Integrity and
  VBS off; automatic Game Mode off; an imported power plan; and the classic
  latency registry values listed in section 7.3.

FormatKit's difference: each item says what it does, what it can break and what
it gains; protected parts of Windows are never touched; every change is
journaled and can be undone; and contested tweaks are labelled as such.

## 2. Scope

**In v1:** categories `apps`, `privacy`, `performance`; levels; per-item
include/exclude in the profile; undo journal; `cleanup list`, `cleanup check`
(read-only diagnosis) and `cleanup undo` commands.

**Later (catalog entries only, no engine change):** categories `ai` (Copilot,
Recall, Click to Do, AI in Edge/Paint/Notepad) and `interface` (file
extensions, classic context menu, taskbar alignment, End Task, Explorer start
location).

**Out of scope:** fixing items found by `cleanup check` (v1 only reports them);
applying settings to the Default User profile (sub-project 5); measuring
latency or frame times; removing Windows components from the install image.

## 3. Decisions

| # | Decision | Reason |
|---|---|---|
| D1 | Tweaks are declarative JSON data interpreted by typed C# action handlers (approach A). | Already decided in the core spec; checkable, reversible, no arbitrary code in data. |
| D2 | The user picks a level, then fine-tunes with `include` / `exclude`. | Simple default, full control. |
| D3 | Levels are cumulative: `safe` ⊂ `balanced` ⊂ `aggressive`. | One mental model. |
| D4 | Any aggressive item selected (by level or by `include`) requires `"confirmAggressive": true` in the profile. | Friends must not enable them by accident. |
| D5 | Contested tweaks (no documented gain) are included only at `aggressive`, flagged `contested`. Tweaks that break things are excluded and documented. | Honest about evidence; the user wanted them available. |
| D6 | The catalog never changes the start type of a protected service, and only removes apps from an allowlist. | The main failure mode of other tools. |
| D7 | `cleanup check` is read-only in v1. | Diagnosis first; fixes later. |
| D8 | Cleanup runs after drivers in the plan. | Windows Update and the Store are used while still untouched. |

## 4. Structure

```
src/FormatKit.Cleanup/
├── CleanupModule.cs          IModule "cleanup": validation, step creation
├── Catalog/
│   ├── cleanup.json          embedded resource: tweak catalog
│   ├── known-issues.json     embedded resource: diagnosis rules
│   ├── CatalogLoader.cs      parses and validates both files
│   └── ProtectedLists.cs     protected services, removable-app allowlist, never-remove list
├── Actions/                  one handler per action type (section 5.3)
├── Journal/                  undo journal store
├── Steps/CleanupCategoryStep.cs
└── Diagnosis/CleanupDiagnosis.cs
```

`FormatKit.Cleanup` depends only on `FormatKit.Core`, like `FormatKit.Drivers`.
New system abstractions (registry, services, AppX, power schemes, optional
features, device-guard state) live in `FormatKit.Core.SystemAccess` behind
interfaces, with fakes for tests, following the existing pattern.

## 5. Catalog

### 5.1 Entry format (`cleanup.json`)

```json
{
  "schemaVersion": 1,
  "items": [
    {
      "id": "privacy.advertising-id",
      "category": "privacy",
      "level": "safe",
      "contested": false,
      "requiresReboot": false,
      "scope": "user",
      "text": {
        "en":    { "title": "...", "what": "...", "breaks": "...", "gain": "..." },
        "pt-BR": { "title": "...", "what": "...", "breaks": "...", "gain": "..." }
      },
      "actions": [
        { "type": "registry", "path": "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo",
          "name": "Enabled", "kind": "dword", "value": 0 }
      ]
    }
  ]
}
```

- `id`: `<category>.<kebab-name>`, unique.
- `category`: `apps` | `privacy` | `performance` (later `ai`, `interface`).
- `level`: `safe` | `balanced` | `aggressive`.
- `scope`: `user` (writes HKCU or per-user state) or `machine`.
- `text`: all four fields required in both languages. `breaks` may say
  "Nothing known." but may not be empty. `gain` for contested items states that
  no gain is documented.
- `laptopSensitive` (optional, default false): power-plan items set it (7.2).

### 5.2 Validation (at load and in tests)

Unique ids; known category, level, scope and action types; complete text in
`en` and `pt-BR`; `contested` only at `aggressive`; no `service` action on a
protected service; `appx` actions only on allowlisted packages; registry paths
start with `HKLM\` or `HKCU\`; `scope: user` items use only HKCU or per-user
actions. A catalog that fails validation is a build failure (test), never a
runtime surprise.

### 5.3 Action types

| Type | Fields | Check | Journal (previous state) | Undo |
|---|---|---|---|---|
| `registry` | `path`, `name`, `kind` (`dword`, `qword`, `string`), `value`, or `delete: true` | value equals target | previous value and kind, or "absent" | restore value, or delete if it was absent |
| `service` | `name`, `startType` (`automatic`, `manual`, `disabled`) | start type equals target | previous start type | restore start type |
| `appx` | `package` (package family name prefix), `storeId` | package absent for the user and not provisioned | package name, `storeId`, whether it was provisioned | `winget install --id <storeId> --source msstore`; on failure, report with the Store link as next action |
| `powerScheme` | `scheme`: `balanced`, `highPerformance`, `ultimate` | active scheme is that built-in scheme | previous active scheme GUID | re-activate the previous scheme; delete any scheme FormatKit created |
| `optionalFeature` | `name`, `state` (`enabled`, `disabled`) | state equals target | previous state | restore state |
| `named` | `handler` (a registered C# handler name) | handler-specific | handler-specific JSON | handler-specific |

Built-in power scheme GUIDs: Balanced `381b4222-f694-41f0-9685-ff5bb260df2e`,
High performance `8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c`, Ultimate Performance
`e9a42b02-d5df-448d-aa00-03f14749eb61`. When the target scheme is not present
(hidden on some systems), it is created with `powercfg /duplicatescheme <guid>`
and the new GUID is journaled as "created by FormatKit". Imported, non-built-in
schemes are never activated.

v1 named handler: `memoryIntegrity` (turns off Memory Integrity and VBS via
`HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled`
and `HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\EnableVirtualizationBasedSecurity`;
`requiresReboot: true`).

### 5.4 Protected lists (`ProtectedLists.cs`, enforced by tests)

- **Protected services** (no `service` action may target them): W32Time,
  wuauserv, UsoSvc, BITS, CryptSvc, TrustedInstaller, defragsvc, InstallService,
  ClipSVC, AppXSvc, LicenseManager, WinDefend, SecurityHealthService, mpssvc,
  BFE, Dnscache, Dhcp, NlaSvc, DsmSvc, DeviceInstall, PlugPlay, Power, Schedule,
  EventLog, XboxGipSvc, XblAuthManager, XblGameSave, ssh-agent, IKEEXT,
  SSDPSRV, StiSvc, SCardSvr, SensorService, vmcompute, vmms, LxssManager,
  WSLService.
- **Never removed apps:** Microsoft.WindowsStore, Microsoft.DesktopAppInstaller,
  Microsoft.MicrosoftEdge*, Microsoft.SecHealthUI, Microsoft.XboxIdentityProvider,
  Microsoft.Xbox.TCUI, Microsoft.Windows.Photos, Microsoft.WindowsCalculator,
  Microsoft.WindowsNotepad, Microsoft.WindowsTerminal, Microsoft.ScreenSketch,
  Microsoft.Paint, Microsoft.VCLibs*, Microsoft.UI.Xaml*, Microsoft.NET.*.
- **Removable-app allowlist:** exactly the packages in section 7.1.

v1 contains no `service` actions at all; the type and the protection exist so
later entries are safe by construction.

## 6. Profile

```json
"cleanup": {
  "enabled": true,
  "level": "balanced",
  "include": ["performance.memory-integrity-off"],
  "exclude": ["apps.xbox"],
  "confirmAggressive": true
}
```

- `level` is required when `enabled` is true.
- `include` / `exclude`: catalog ids; unknown ids are profile errors with the
  usual path (`modules.cleanup.include[0]: unknown cleanup item "x"`). An id in
  both lists is an error.
- Selected items = items at or below `level`, plus `include`, minus `exclude`.
- If any selected item is `aggressive` and `confirmAggressive` is not `true`,
  validation fails with a message naming those items.

## 7. Initial catalog content

Exact registry locations and Store ids are verified against Microsoft
documentation during implementation; an item whose location cannot be
verified is dropped, not guessed.

### 7.1 Apps (`apps.*`, `appx` actions)

| Level | Items (package) |
|---|---|
| safe | Clipchamp (`Clipchamp.Clipchamp`), Solitaire (`Microsoft.MicrosoftSolitaireCollection`), News (`Microsoft.BingNews`), Weather (`Microsoft.BingWeather`), Get Help (`Microsoft.GetHelp`), Tips (`Microsoft.Getstarted`), Feedback Hub (`Microsoft.WindowsFeedbackHub`), Power Automate (`Microsoft.PowerAutomateDesktop`), Teams personal (`MSTeams`) |
| balanced | Xbox app (`Microsoft.GamingApp`), Phone Link (`Microsoft.YourPhone`), To Do (`Microsoft.Todos`) |

### 7.2 Privacy (`privacy.*`)

| Level | Items |
|---|---|
| safe | advertising ID off; tailored experiences off; suggestions and ads in Start, lock screen and Settings off; tips notifications off; silently installed suggested apps off; activity history off; inking and typing personalization off; feedback frequency "never" |
| balanced | diagnostic data at the minimum the edition allows (Required on Home/Pro); web search suggestions in Start off; online speech recognition off |

### 7.3 Performance (`performance.*`)

| Level | Items |
|---|---|
| safe | Game Mode on (`AutoGameModeEnabled=1`); hardware-accelerated GPU scheduling on (`HwSchMode=2`, reboot); Game Bar background recording off |
| balanced | High performance power plan (laptopSensitive); enhanced pointer precision off |
| aggressive | Ultimate Performance power plan (laptopSensitive); Memory Integrity and VBS off (named `memoryIntegrity`, reduces security, reboot); contested: `SystemResponsiveness`, `NetworkThrottlingIndex`, "Games" multimedia task priority, `Win32PrioritySeparation`, `PowerThrottlingOff` |

`laptopSensitive` items are skipped on machines with a battery (WMI
`Win32_Battery` present) unless listed in `include`, and their `breaks` text
mentions battery life.

### 7.4 Deliberately not included

Documented in `docs/cleanup-not-included.md`, each with the reason, and all
detected by `cleanup check`: disabling HPET or `bcdedit useplatformclock`;
disabling any device; disabling services; `DisablePagingExecutive`; Nagle
(`TcpAckFrequency`, `TCPNoDelay`); `MouseDataQueueSize` /
`KeyboardDataQueueSize`; `GlobalTimerResolutionRequests`; registry cleaners;
turning off Virtual Machine Platform (breaks WSL and Hyper-V, needed by
sub-project 4); disabling Windows Update or Defender.

## 8. Applying

### 8.1 Steps

`CleanupModule.CreateSteps` returns up to three `CleanupCategoryStep`s, in
order Apps, Privacy, Performance, each holding that category's selected items
(a category with none selected contributes no step). Step ids:
`cleanup.apps`, `cleanup.privacy`, `cleanup.performance`. All require admin and
none is critical.

- `CheckAsync` is true when every item's actions already match.
- `ExecuteAsync` applies each item and returns one message per item
  (applied / already in place / skipped with reason / failed with reason).
  Outcome: `Failed` if any item failed, else `RebootRequired` if any applied
  item has `requiresReboot`, else `Succeeded` (or `Skipped` if nothing was
  applied).

### 8.2 One item

1. For each action: check; if already at target, record nothing.
2. Read the current state and append it to the journal (written to disk
   before the change).
3. Apply, then re-read to verify.
4. If an action fails, undo the item's actions already applied in this
   attempt, using the journal entries just written, and mark the item failed.

### 8.3 Per-user items

`scope: user` items write the HKCU of the process. When the elevated process's
user differs from the interactive console session's user (elevation with a
different admin account), user items are skipped with a warning naming the
reason; machine items still apply.

## 9. Undo journal

- File: `C:\ProgramData\FormatKit\cleanup-journal.json`, written atomically
  through the existing file abstraction, UTF-8.
- Content: list of entries `{ itemId, appliedAt, actions: [{ type, target,
  previous }] }`, append-only. Undo marks entries as reverted with a
  timestamp instead of deleting them.
- A corrupt journal is reported by `check` and `undo` with its path; it never
  blocks `list` or `check`, and `run` refuses to apply cleanup items (failed
  step with next action "inspect or move the journal file").
- Undo of an item restores the earliest unreverted previous state recorded
  for each target, so applying twice and undoing once returns to the original.

## 10. Commands

| Command | Elevation | Behavior | Exit codes |
|---|---|---|---|
| `formatkit cleanup list [--level <l>] [--category <c>]` | no | Catalog as a table: id, level, contested mark, title; `--verbose` adds what/breaks/gain | 0, 2 |
| `formatkit cleanup check` | no (items needing admin to read are shown as "not checked") | Diagnosis, section 11 | 0 none found in "breaks" or "security"; 1 otherwise |
| `formatkit cleanup undo <id>... \| --all` | yes | Reverts journaled items, newest first | 0, 1 any failed, 2, 5 |
| `formatkit run --profile x.json` | yes | Applies selected items through the engine | as in the core spec |

`--dry-run` on `run` lists the cleanup items that would be applied, with their
level and contested mark.

## 11. Diagnosis (`cleanup check`)

Rules in `known-issues.json`, same bilingual text shape (`title`, `breaks`,
`fix` with a copyable command). Rule kinds:

| Kind | Example rules |
|---|---|
| `deviceDisabled` (by hardware id) | `ROOT\VDRVROOT`, `ROOT\UMBUS`, `ROOT\NDISVIRTUALBUS`, HPET (`ACPI\PNP0103`) |
| `serviceDisabled` | every protected service from 5.4 |
| `registryValue` | Windows Update auto-update off by policy, Defender off by policy, Memory Integrity off, VBS off, automatic Game Mode off, each contested and not-included value from 7.3/7.4 |
| `powerScheme` | active scheme is not a built-in scheme |

Output groups, in order: **Breaks something**, **Reduces security**,
**Contested**, **Informational**; then **FormatKit items applied** (from the
journal, with "can be undone" yes/no). Each finding prints title, what it
breaks and the fix command. Hardware ids are matched, not names, so the check
works in any display language.

## 12. Error handling

- Item failures never stop the run; they end as `CompletedWithFailures` with a
  next action.
- A target missing on this Windows build (app not installed, key path absent
  for a delete) is "already in place" for removal and "skipped: not present"
  otherwise.
- `winget` missing during undo: failed item with next action "install App
  Installer from the Store, then rerun undo".
- Every action logs before/after at Debug and the outcome at Information.

## 13. Testing

- **Catalog tests:** load the embedded catalog and rules; assert section 5.2
  rules, protected lists, allowlist, and that every `contested` item is
  `aggressive`.
- **Action handlers:** each against fakes: already at target, apply + journal
  + verify, failure rolls back earlier actions of the item, undo restores
  value, undo of "absent" deletes, power scheme created-by-FormatKit is
  deleted on undo.
- **Selection:** level/include/exclude math, `confirmAggressive`, laptop skip,
  per-user skip.
- **Journal:** JSON round-trip, append-only, earliest-previous-state rule,
  corrupt file behavior.
- **Diagnosis:** a fake system reproducing the author's PC (section 1) yields
  the expected groups; a clean fake system yields none.
- **CLI:** `cleanup list`, `check` exit codes, `undo` argument validation.
- **VM scenarios** added to `docs/testing.md`: export HKLM/HKCU, run with
  `aggressive` + `confirmAggressive`, reboot/resume, `cleanup check`,
  `cleanup undo --all`, export again and compare (identical except volatile
  keys listed in the doc); remove and reinstall one app; on a VM with HPET and
  `ROOT\VDRVROOT` disabled, `check` reports both.

## 14. Sources consulted

- Win11Debloat: https://github.com/Raphire/Win11Debloat
- WinUtil: https://winutil.christitus.com/
- privacy.sexy: https://github.com/undergroundwires/privacy.sexy
- Sophia Script: https://github.com/ClickToConfirm/Sophia-Script-for-Windows
- Debloat breakage reports: https://windowsforum.com/news/debloating-windows-11-why-a-cleaner-pc-can-break-updates.383570/ ,
  https://forum.level1techs.com/t/i-broke-windows-10-by-running-a-powershell-script/151100
- Microsoft gaming guidance on Memory Integrity / VMP (press coverage):
  https://www.techradar.com/news/are-you-a-gamer-running-windows-11-heres-microsofts-advice-to-speed-up-games
- HPET discussion: https://forums.blurbusters.com/viewtopic.php?p=88188
