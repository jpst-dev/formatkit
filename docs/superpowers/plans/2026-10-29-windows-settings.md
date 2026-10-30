# Windows Settings and Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the cleanup catalog and engine so one profile also applies the author's Windows settings (taskbar, Explorer, AI, ads, theme, keyboard, sound, power) and the research-backed optimizations, all checked, journaled and undoable.

**Architecture:** New catalog fields (`optIn`, `restartsExplorer`, `condition`), registry `binary` kind and default values, three new action types (`scheduledTask`, `startupItem`, `powerSetting`), a `formatkit` power scheme and three named handlers, each an `IActionHandler` behind `SystemAccess` interfaces with fakes. Then the catalog content, the "Reverted by Windows" check group and the VM scenarios.

**Tech Stack:** .NET 10, C#, xunit.v3, System.Text.Json, `powercfg`, PowerShell (`Get-ScheduledTask`, `Get-WinUserLanguageList`, `Get-Printer`), Hyper-V test VM.

**Spec:** `docs/superpowers/specs/2026-10-29-windows-settings-design.md` (extends `docs/superpowers/specs/2026-10-27-cleanup-design.md`).

## Global Constraints

- `TreatWarningsAsErrors` is on: a warning breaks the build.
- Unit tests before every commit: `dotnet test -- --filter-not-trait "Category=Integration"`.
- Everything through `SystemAccess` interfaces; no direct `Registry`, `File` or `Process` calls in handlers or steps.
- New `ServiceSet` members go last, wired in `src/FormatKit.Hosting/Services.cs` and `tests/FormatKit.Core.Tests/Fakes/TestServices.cs` (optional parameter with a fake default).
- PowerShell literals built in C# go through `PowerShellText.SingleQuote`.
- User-facing text in `Resources/Strings.resx` with the matching `Strings.pt-BR.resx` entry; catalog text in `en` and `pt-BR`.
- Code, comments, docs and commits in English; conventional commits ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Never run system-changing commands on the host; integration of real Windows behavior is verified only in the test VM.
- Existing item ids never change (spec D8). An item whose registry location cannot be verified is dropped (spec D9).

## Review Focus

1. A profile that `include`s an `optIn` item at `aggressive` level without `confirmAggressive` must fail validation, like any aggressive item.
2. Undo after Windows already reverted a setting (state equals the journaled previous value) must succeed silently, not fail.
3. `startupItem` on a machine where the Run value is absent: "already in place", and undo must not create a StartupApproved value that was never there.
4. `powerSetting` undo when the scheme it wrote has since been deleted (the FormatKit plan undone first): skip with a log line, not a failure.
5. `keyboardLayout` when the current list has pt-BR with several input methods and another language (en-US): only pt-BR's inputs change; en-US stays in place.

Each line has its test in the owning task (Tasks 1, 3, 4, 5, 7).

---

### Task 1: Catalog model, validation and selection

**Files:**
- Modify: `src/FormatKit.Cleanup/Catalog/CleanupCatalog.cs`, `src/FormatKit.Cleanup/Catalog/CatalogValidator.cs`, `src/FormatKit.Cleanup/ItemSelector.cs`, `src/FormatKit.Cleanup/Steps/CleanupCategoryStep.cs` (order only), `src/FormatKit.Cleanup/CleanupModule.cs` (step order), `src/FormatKit.Cleanup/Resources/Strings.resx` + `Strings.pt-BR.resx`
- Test: `tests/FormatKit.Cleanup.Tests/Catalog/CatalogValidatorTests.cs`, `ItemSelectorTests.cs`, `CleanupModuleTests.cs`, `TestItems.cs`

**Interfaces:**
- Produces: `enum CleanupCategory { Apps, Startup, Privacy, Ai, Interface, Appearance, Input, Sound, Performance, Power, Developer }` (declaration order = step order, spec 8); `enum ItemCondition { NoPrinter }`; `CleanupItem` gains trailing optional parameters `bool OptIn = false, bool RestartsExplorer = false, ItemCondition? Condition = null`; `CleanupItem.IsOptIn => OptIn || Category == CleanupCategory.Developer`.

- [ ] **Step 1: Write failing tests**
  - Validator: ids `startup.x`, `interface.x`, `ai.x`, `appearance.x`, `input.x`, `sound.x`, `power.x` are valid; `laptopSensitive` is accepted with a `powerSetting` action or `named hibernate`; `condition: "noPrinter"` parses; an unknown condition fails JSON parsing; `scope: user` accepts `startupItem` with scope user and `named keyboardLayout`/`soundScheme`, and still rejects an HKLM registry action.
  - Selector: `Select_OptInItem_NotSelectedByLevel`, `Select_OptInItem_SelectedByInclude`, `Select_DeveloperItem_StillIncludeOnly`.
  - Module validation: `Validate_IncludedAggressiveOptIn_RequiresConfirmAggressive` (Review Focus 1).
  - Module: `CreateSteps_OrdersStepsByCategoryDeclaration` (apps before startup before privacy ... before developer).
- [ ] **Step 2: Run them, expect failures** (`dotnet test tests/FormatKit.Cleanup.Tests`).
- [ ] **Step 3: Implement.** Id regex becomes `^(apps|startup|privacy|ai|interface|appearance|input|sound|performance|power|developer)\.[a-z0-9-]+$`. Selector replaces the `Category != Developer` test with `!i.IsOptIn`. Step titles `Step_Title_Startup`, `_Ai`, `_Interface`, `_Appearance`, `_Input`, `_Sound`, `_Power` in both resx files (pt-BR: Inicialização, IA, Interface, Aparência, Entrada, Som, Energia).
- [ ] **Step 4: Run tests, expect PASS.**
- [ ] **Step 5: Commit** `feat(cleanup): add the new categories and opt-in items`.

### Task 2: Registry binary kind and default value

**Files:**
- Modify: `src/FormatKit.Core/SystemAccess/IRegistry.cs`, `WindowsRegistry.cs`, `tests/FormatKit.Core.Tests/Fakes/FakeRegistry.cs`, `src/FormatKit.Cleanup/Actions/RegistryHandler.cs`, `src/FormatKit.Cleanup/Catalog/CatalogValidator.cs`
- Test: `tests/FormatKit.Cleanup.Tests/Actions/RegistryHandlerTests.cs`, `Catalog/CatalogValidatorTests.cs`, `tests/FormatKit.Core.Tests/SystemAccess/WindowsRegistryTests.cs` (Integration trait, HKCU test key only)

**Interfaces:**
- Produces: `RegistryKind.Binary`, whose `RegistryValue.Data` is lowercase hex without separators; `name == ""` addresses the default value in `Read`/`Write`/`Delete`.

- [ ] **Step 1: Failing tests:** handler applies and undoes a binary value (`"0300000000000000"`), compares case-insensitively on read; default value write/undo-to-absent; validator rejects odd-length or non-hex binary values.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.** `WindowsRegistry` maps `Binary` to `RegistryValueKind.Binary` with `Convert.FromHexString`/`Convert.ToHexStringLower`; JSON `kind: "binary"` with string `value`.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(cleanup): support binary and default registry values`.

### Task 3: `scheduledTask` action

**Files:**
- Create: `src/FormatKit.Core/SystemAccess/IScheduledTaskControl.cs`, `PowerShellScheduledTaskControl.cs`, `tests/FormatKit.Core.Tests/Fakes/FakeScheduledTaskControl.cs`, `src/FormatKit.Cleanup/Actions/ScheduledTaskHandler.cs`, `tests/FormatKit.Cleanup.Tests/Actions/ScheduledTaskHandlerTests.cs`
- Modify: `ServiceSet` (new last member `IScheduledTaskControl TaskControl`), `src/FormatKit.Hosting/Services.cs`, `TestServices.cs`, `CleanupActions.cs`, `ActionHandlers.cs`, `ProtectedLists.cs`, `CatalogValidator.cs`

**Interfaces:**
- Produces: `public sealed record ScheduledTaskAction(string Path, string Name, bool Enabled) : CleanupAction` (`"scheduledTask"`); `interface IScheduledTaskControl { Task<bool?> IsEnabledAsync(string path, string name, CancellationToken ct); Task SetEnabledAsync(string path, string name, bool enabled, CancellationToken ct); }` (null = task absent); `ProtectedLists.IsProtectedTask(string path, string name)`; journal target `"<path><name>"`.

- [ ] **Step 1: Failing tests:** applied when state matches; absent task → handler throws `TargetNotPresentException` (existing "skipped: not present" path); apply journals previous and verifies; undo restores; undo when already at previous state succeeds (Review Focus 2); validator rejects every spec 5.3 protected task and folder prefix; a path without leading and trailing `\` is rejected.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.** The PowerShell implementation runs `Get-ScheduledTask -TaskPath <p> -TaskName <n> -ErrorAction SilentlyContinue` and `Enable-/Disable-ScheduledTask` through `IProcessRunner`, literals via `PowerShellText.SingleQuote`; state `Disabled` means not enabled.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(cleanup): add the scheduledTask action`.

### Task 4: `startupItem` action

**Files:**
- Create: `src/FormatKit.Cleanup/Actions/StartupItemHandler.cs`, `tests/FormatKit.Cleanup.Tests/Actions/StartupItemHandlerTests.cs`
- Modify: `CleanupActions.cs`, `ActionHandlers.cs`, `CatalogValidator.cs`

**Interfaces:**
- Consumes: `RegistryKind.Binary` (Task 2).
- Produces: `public sealed record StartupItemAction(string Name, ItemScope Scope) : CleanupAction` (`"startupItem"`). Run key `HK{CU|LM}\Software\Microsoft\Windows\CurrentVersion\Run`; approval value at `...\Explorer\StartupApproved\Run`; disabled = 12 bytes, first byte `03`, rest zero.

- [ ] **Step 1: Failing tests:** Run value absent → applied, nothing journaled, and undo of such an item creates nothing (Review Focus 3); enabled (`02...` or absent approval) → apply writes `030000000000000000000000`, journals previous bytes or absent; undo restores bytes or deletes the approval value; the Run value is never written or deleted (fake registry records no write to the Run key).
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement** with `ctx.Services.Registry` only.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(cleanup): add the startupItem action`.

### Task 5: `powerSetting` action

**Files:**
- Modify: `src/FormatKit.Core/SystemAccess/IPowerSchemes.cs`, `PowerCfg.cs`, `FakePowerSchemes.cs`, `CleanupActions.cs`, `ActionHandlers.cs`, `ItemSelector.cs`, `CatalogValidator.cs`
- Create: `src/FormatKit.Cleanup/Actions/PowerSettingHandler.cs`, `tests/FormatKit.Cleanup.Tests/Actions/PowerSettingHandlerTests.cs`, `tests/FormatKit.Core.Tests/SystemAccess/PowerCfgParseTests.cs`

**Interfaces:**
- Produces: `IPowerSchemes` gains `Task<(int Ac, int Dc)?> GetValueAsync(Guid scheme, string subgroup, string setting, CancellationToken ct)` (null when the setting is absent) and `Task SetValueAsync(Guid scheme, string subgroup, string setting, int ac, int? dc, CancellationToken ct)`; `public sealed record PowerSettingAction(string Subgroup, string Setting, int Ac, int? Dc) : CleanupAction` (`"powerSetting"`); journal target `"power:<subgroup>/<setting>"`, previous `{ scheme, ac, dc }`.

- [ ] **Step 1: Failing tests:** `PowerCfg` parses `powercfg /q <scheme> <sub> <setting>` output for "Current AC Power Setting Index: 0x00000384" in English and the pt-BR wording captured from the VM (`Índice de Configuração de Energia CA Atual`); handler writes the active scheme and journals it; undo writes the journaled scheme; undo when that scheme no longer exists logs and succeeds (Review Focus 4); selector keeps only the last of two items with the same subgroup/setting and logs the dropped id.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.** `SetValueAsync` runs `powercfg /setacvalueindex` (and `/setdcvalueindex` when `dc` is set), then `powercfg /setactive <scheme>` when the scheme is active, so the change takes effect.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(cleanup): add the powerSetting action`.

### Task 6: `formatkit` power scheme

**Files:**
- Modify: `IPowerSchemes.cs` (`Task RenameAsync(Guid scheme, string name, string description, CancellationToken ct)`), `PowerCfg.cs`, `FakePowerSchemes.cs`, `PowerSchemeHandler.cs`, `CatalogValidator.cs`, `Strings.resx` + `Strings.pt-BR.resx` (`PowerScheme_FormatKit_Name` = "FormatKit Performance" / "FormatKit Desempenho", `_Description`)
- Test: `tests/FormatKit.Cleanup.Tests/Actions/PowerSchemeHandlerTests.cs`

**Interfaces:**
- Consumes: `IPowerSchemes.SetValueAsync`/`GetValueAsync` (Task 5).
- Produces: scheme name `formatkit`; `PowerSchemeHandler.FormatKitSettings`, the six `(subgroup, setting, value)` tuples of spec 5.4: (`SUB_PROCESSOR`, `CPMINCORES`, 100), (`SUB_PROCESSOR`, `PERFEPP`, 0), (`SUB_PROCESSOR`, `PERFBOOSTMODE`, 2), (`SUB_PCIEXPRESS`, `ASPM`, 0), (`2a737441-1930-4402-8d77-b2bebba308a3`, `48e6b7a6-50f5-4782-a5d4-53bb8f07e226`, 0), (`SUB_DISK`, `DISKIDLE`, 0); AC and DC both.

- [ ] **Step 1: Failing tests:** first apply duplicates High performance, renames, writes six settings, activates, journals created-by-FormatKit with source `formatkit`; second run reuses the created scheme; check false when one value drifted; undo re-activates previous and deletes the scheme; selector: an included `formatkit` item wins over the level's `highPerformance` item.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement** by extending `PowerSchemeHandler` (source marker for `formatkit` is the High performance GUID plus a `"formatkit"` flag in the journal's previous JSON, so older journals keep parsing). The selector's "highest level wins" rule prefers an included item over a level-selected one.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(cleanup): add the FormatKit power plan`.

### Task 7: Named handlers `keyboardLayout`, `soundScheme`, `hibernate`

**Files:**
- Create: `src/FormatKit.Cleanup/Actions/KeyboardLayoutHandler.cs`, `SoundSchemeHandler.cs`, `HibernateHandler.cs` and one test file each under `tests/FormatKit.Cleanup.Tests/Actions/`
- Modify: `ActionHandlers.cs`, `ProtectedLists.cs` (`NamedHandlers`), `CatalogValidator.cs` (user-scope handler set)

**Interfaces:**
- Produces: names `keyboardLayout`, `soundScheme`, `hibernate`; journal targets equal the names.

- [ ] **Step 1: Failing tests.**
  - Keyboard (fake process runner returning JSON from `Get-WinUserLanguageList | ConvertTo-Json`): pt-BR first with `0416:00020409` only → applied; pt-BR with ABNT2 and US-Intl plus en-US → apply sets pt-BR to `0416:00020409` and keeps en-US unchanged after it (Review Focus 5); no pt-BR → added first; `Set-WinUserLanguageList` failure → handler throws and the previous list is restored; undo restores the journaled list.
  - Sound: applies `.None` and clears `.Current` values under `HKCU\AppEvents\Schemes\Apps`; undo restores each value; check false when one `.Current` is not empty.
  - Hibernate: check reads `HKLM\SYSTEM\CurrentControlSet\Control\Power\HibernateEnabled`; apply runs `powercfg /hibernate off` and verifies; undo runs `/hibernate on` only when previous was 1.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement** through `IProcessRunner` (`powershell.exe -NoProfile -NonInteractive -Command`, script literals via `PowerShellText.SingleQuote`) and `IRegistry`.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(cleanup): add keyboard layout, sound scheme and hibernation handlers`.

### Task 8: Step behavior: Explorer restart and `noPrinter`

**Files:**
- Create: `src/FormatKit.Core/SystemAccess/IPrinters.cs`, `PowerShellPrinters.cs`, `tests/FormatKit.Core.Tests/Fakes/FakePrinters.cs`
- Modify: `ServiceSet` (last member `IPrinters Printers`), `Services.cs`, `TestServices.cs`, `CleanupCategoryStep.cs`, `Strings.resx` + `Strings.pt-BR.resx`
- Test: `tests/FormatKit.Cleanup.Tests/Steps/CleanupCategoryStepTests.cs`

**Interfaces:**
- Produces: `interface IPrinters { Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct); }`; `CleanupCategoryStep` skip reason `Skip_PrinterInstalled`; warning `Warning_ExplorerRestartFailed`.

- [ ] **Step 1: Failing tests:** two `restartsExplorer` items applied → exactly one `taskkill /f /im explorer.exe` and one `cmd.exe /c start "" explorer.exe` through the fake runner, after the last item; no applied `restartsExplorer` item (all already in place) → no restart; restart failure → step still `Succeeded` with the warning message; `noPrinter` item skipped when the fake lists "HP LaserJet", applied when it lists only "Microsoft Print to PDF", "Microsoft XPS Document Writer", "OneNote (Desktop)" and "Fax".
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.** Virtual printers are matched by these exact names plus the prefix "OneNote".
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(cleanup): restart Explorer and skip printer items when needed`.

### Task 9: `cleanup check` — Reverted by Windows

**Files:**
- Modify: `src/FormatKit.Cleanup/Diagnosis/CleanupDiagnosis.cs`, the CLI check output in `src/FormatKit.Cli` (group heading), `Strings.resx` + `Strings.pt-BR.resx`
- Test: `tests/FormatKit.Cleanup.Tests/Diagnosis/CleanupDiagnosisTests.cs`

**Interfaces:**
- Consumes: `ItemApplier.IsAppliedAsync` for each journaled, unreverted item still in the catalog.
- Produces: diagnosis group `RevertedByWindows`, ordered before the applied-items group; finding fix text `formatkit run --profile <profile>`.

- [ ] **Step 1: Failing tests:** journal with two applied items, fake state where one drifted → one finding in the new group; an item no longer in the catalog is ignored; an item whose check throws (needs admin) is listed as "not checked", not as reverted; exit code unchanged (the group is informational).
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(cleanup): report items Windows reverted`.

### Task 10: Catalog content, docs and examples

**Files:**
- Modify: `src/FormatKit.Cleanup/Catalog/Data/cleanup.json`, `ProtectedLists.cs` (`RemovableApps` += `MicrosoftWindows.Client.WebExperience`, `Microsoft.OutlookForWindows`, `Microsoft.Windows.DevHome`, `Microsoft.BingSearch`, `Microsoft.Copilot`, `Microsoft.MicrosoftOfficeHub`, `MicrosoftWindows.CrossDevice`), `docs/cleanup-not-included.md`, `src/FormatKit.Cli` `cleanup list` (opt-in mark), the WPF cleanup list view model (opt-in badge), `README.md` (new categories)
- Create: `profiles/windows-setup-example.json`
- Test: `tests/FormatKit.Cleanup.Tests/Catalog/EmbeddedContentTests.cs`

- [ ] **Step 1: Failing tests:** every spec section 7 id exists with the spec's level, `contested`, `optIn`, `laptopSensitive`, `restartsExplorer` and `condition`; `windows-setup-example.json` validates; `cleanup list` marks opt-in items.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Write the entries.** Each registry location is checked against Microsoft documentation (policy CSP / ADMX reference) or, for HKCU Settings values, read back in the VM after toggling the setting in the Settings app. Unverifiable entries are left out and listed in the task report. Text follows the existing tone: `what`, `breaks` (never empty), `gain` (contested items say no gain is documented). Store ids for new appx items come from `winget search --source msstore` run in the VM.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(cleanup): add the Windows settings catalog entries`.

### Task 11: VM scenarios

**Files:**
- Modify: `docs/testing.md` (scenarios from spec 11, numbered 28-30 there; 23-27 belong to the installation media sub-project), `docs/superpowers/specs/2026-10-29-windows-settings-design.md` section 12

- [ ] **Step 1:** Publish (`dotnet publish src/FormatKit.Cli -c Release -o artifacts`), restore the `clean` checkpoint, copy `formatkit.exe` and `profiles/windows-setup-example.json`.
- [ ] **Step 2: Scenario 1.** `vm.ps1 job` runs `formatkit run --profile windows-setup-example.json` on the user's desktop; after it ends, `vmshot.ps1 -Name settings-desktop` shows the left taskbar, dark theme and search icon; `Get-WinUserLanguageList` shows pt-BR with `0416:00020409`; `cleanup check` reports nothing under "Reverted by Windows"; the research `measure.ps1` sample is recorded next to G4r.
- [ ] **Step 3: Scenario 2.** `formatkit cleanup undo --all`; registry export compare as in the cleanup spec; keyboard back to ABNT2; `powercfg /list` without the FormatKit plan.
- [ ] **Step 4: Scenario 3.** Re-run the profile: every item "already in place".
- [ ] **Step 5:** Every fix found gets a unit test first; record findings in spec section 12; commit `docs: record the Windows settings VM runs`.
