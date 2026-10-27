# FormatKit Core and Drivers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the FormatKit command-line tool with a resumable, profile-driven step engine and a drivers module that detects hardware and installs current drivers.

**Architecture:** `FormatKit.Core` holds the profile loader, step engine, run state, reporting, logging and every operating-system abstraction. `FormatKit.Drivers` depends only on Core and contributes driver steps. `FormatKit.Cli` is the composition root and the single-file executable. Tests use fakes of the Core interfaces and never touch the real system, except opt-in read-only integration tests.

**Tech Stack:** .NET 10 (`net10.0-windows`), C#, System.CommandLine 2.x, Serilog (Console + File sinks), System.Management (WMI), Windows Update Agent COM API, xUnit v3, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-10-27-core-and-drivers-design.md`

## Global Constraints

- Target framework `net10.0-windows`, `RuntimeIdentifier` `win-x64`, nullable enabled, warnings as errors.
- The CLI publishes as a single self-contained file: `PublishSingleFile=true`, `SelfContained=true`.
- `FormatKit.Drivers` references only `FormatKit.Core`. Modules never reference each other.
- Code, identifiers, comments, commits and README are in English.
- User-facing strings come from `.resx` resources: `Strings.resx` (English, default) and `Strings.pt-BR.resx`. Never hard-code user-facing text in code.
- Unit tests never start real processes, query WMI, call Windows Update, touch the registry or write outside a temp directory.
- Profile `schemaVersion` supported by this version: `1`.
- Data root: `%ProgramData%\FormatKit` → `state.json`, `logs\run-<yyyyMMdd-HHmmss>.log`, `reports\<yyyyMMdd-HHmmss>\report.json` and `report.md`.
- Timeouts: installs 30 minutes, queries 2 minutes.
- Reboot countdown: 60 seconds, cancelable.
- Scheduled task name: `FormatKit\Resume`, runs `"<exe>" run --resume` at logon with highest privileges.
- Never use third-party driver updater programs.
- License MIT.

## Review Focus

1. **Corrupt or truncated `state.json`** (manual edit, disk error): `run --resume` must report it clearly and suggest a new run, never crash with a stack trace. Pinned in Task 4.
2. **Broken profile file** (missing file, invalid JSON, wrong types, extra module): every problem is reported with its JSON path and nothing runs. Pinned in Task 2.
3. **No connectivity and no `networkDriverBackup` configured**: the critical network step fails with an actionable message and the run stops. Pinned in Task 11.
4. **Placeholder or virtual-machine WMI data** (empty manufacturer, `To Be Filled By O.E.M.`, `Microsoft Corporation` VM): the machine is classified as a custom build with no OEM tool and nothing crashes. Pinned in Task 9.
5. **A step throws an unexpected exception**: it is recorded as Failed with the exception message, and the run continues unless the step is critical. Pinned in Task 5.

---

## File Map

```
formatkit/
├── FormatKit.sln
├── Directory.Build.props                 shared settings (TFM, nullable, warnings as errors)
├── .gitignore  .editorconfig  LICENSE  README.md
├── .github/workflows/ci.yml  release.yml
├── profiles/example.json
├── docs/testing.md
├── src/FormatKit.Core/
│   ├── AppPaths.cs  Timeouts.cs
│   ├── Localization/Strings.cs  Resources/Strings.resx  Resources/Strings.pt-BR.resx
│   ├── Profiles/   Profile.cs  ProfileError.cs  ProfileLoader.cs  IModule.cs  PlanBuilder.cs  BuiltInProfiles.cs
│   ├── Steps/      IStep.cs  StepResult.cs  StepContext.cs  ServiceSet.cs
│   ├── Running/    RunState.cs  RunStateStore.cs  RunEngine.cs  RunRequest.cs  RunSummary.cs
│   ├── Reporting/  ReportWriter.cs
│   ├── Logging/    LogSetup.cs
│   ├── SystemAccess/  IProcessRunner.cs  ProcessRunner.cs  IFileSystem.cs  FileSystem.cs
│   │                  IScheduledTasks.cs  SchtasksScheduledTasks.cs  IRebooter.cs  ConsoleRebooter.cs
│   │                  IElevation.cs  Elevation.cs  INetworkProbe.cs  HttpNetworkProbe.cs
│   │                  WingetClient.cs  PnpUtil.cs
│   ├── Hardware/   HardwareSnapshot.cs  IHardwareInfo.cs  WmiHardwareInfo.cs  GpuVendors.cs
│   └── WindowsUpdate/ IWindowsUpdateClient.cs  ComWindowsUpdateClient.cs  WindowsUpdateErrors.cs
├── src/FormatKit.Drivers/
│   ├── DriversModule.cs  DriversOptions.cs  DriverScanner.cs  NetworkDriverBackup.cs
│   ├── Catalog/    VendorCatalog.cs  MachineClassifier.cs  Data/vendors.json
│   ├── Steps/      NetworkCheckStep.cs  WindowsUpdateDriversStep.cs  OemToolStep.cs  GpuStep.cs  LeftoverReportStep.cs
│   └── Resources/Strings.resx  Strings.pt-BR.resx  (+ Localization/Strings.cs)
├── src/FormatKit.Cli/
│   ├── Program.cs  CliApp.cs  ExitCodes.cs  Services.cs
│   └── Commands/   HardwareCommand.cs  DriversCommands.cs  RunCommand.cs
└── tests/
    ├── FormatKit.Core.Tests/   (Fakes/ folder shared by test files)
    ├── FormatKit.Drivers.Tests/
    └── FormatKit.Cli.Tests/
```

Shared fakes live in `tests/FormatKit.Core.Tests/Fakes/` and are linked into the other test projects with `<Compile Include="..\FormatKit.Core.Tests\Fakes\*.cs" LinkBase="Fakes" />`.

---

### Task 1: Solution scaffold and process runner

**Files:**
- Create: `FormatKit.sln`, `Directory.Build.props`, `.gitignore`, `.editorconfig`, `LICENSE`, `.github/workflows/ci.yml`
- Create: `src/FormatKit.Core/FormatKit.Core.csproj`, `src/FormatKit.Drivers/FormatKit.Drivers.csproj`, `src/FormatKit.Cli/FormatKit.Cli.csproj`
- Create: `tests/FormatKit.Core.Tests/…csproj`, `tests/FormatKit.Drivers.Tests/…csproj`, `tests/FormatKit.Cli.Tests/…csproj`
- Create: `src/FormatKit.Core/SystemAccess/IProcessRunner.cs`, `ProcessRunner.cs`, `src/FormatKit.Core/Timeouts.cs`
- Create: `tests/FormatKit.Core.Tests/Fakes/FakeProcessRunner.cs`
- Test: `tests/FormatKit.Core.Tests/SystemAccess/ProcessRunnerTests.cs`

**Interfaces:**
- Produces:
  - `record ProcessSpec(string FileName, IReadOnlyList<string> Arguments, TimeSpan Timeout)`
  - `record ProcessResult(int ExitCode, string StdOut, string StdErr, TimeSpan Duration, bool TimedOut)`
  - `interface IProcessRunner { Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct); }` — throws `FileNotFoundException` when the executable does not exist.
  - `class ProcessRunner(Serilog.ILogger log) : IProcessRunner`
  - `static class Timeouts { TimeSpan Install = 30 min; TimeSpan Query = 2 min; }`
  - `class FakeProcessRunner : IProcessRunner` — records every `ProcessSpec` in `Calls`; returns results from a queue configured with `Enqueue(string fileName, ProcessResult result)`; `ThrowNotFound(string fileName)` makes that executable throw `FileNotFoundException`.

- [ ] **Step 1: Install the .NET 10 SDK if missing**

Run: `dotnet --list-sdks`. If no `10.` line: `winget install --id Microsoft.DotNet.SDK.10 --exact --silent --accept-package-agreements --accept-source-agreements`, then open a new shell.
Expected: a `10.0.x` SDK listed.

- [ ] **Step 2: Create the solution and projects**

`Directory.Build.props`: `TargetFramework=net10.0-windows`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`. Cli csproj: `OutputType=Exe`, `AssemblyName=formatkit`, `RuntimeIdentifier=win-x64`. Test projects: xUnit v3 (`dotnet new xunit3` template or packages `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`). Add packages: Core → `Serilog`, `Serilog.Sinks.Console`, `Serilog.Sinks.File`, `System.Management`; Cli → `System.CommandLine` (2.x stable). Project references follow the Global Constraints. `.gitignore` from `dotnet new gitignore`. `LICENSE`: MIT, copyright holder "FormatKit contributors".

Run: `dotnet build`
Expected: `Build succeeded` with 0 warnings.

- [ ] **Step 3: Write failing tests for `ProcessRunner`**

```csharp
[Fact] async Task Captures_exit_code_and_stdout()
// RunAsync(new("cmd.exe", ["/c", "echo hello& exit 3"], TimeSpan.FromSeconds(10)))
// → ExitCode == 3, StdOut.Trim() == "hello", TimedOut == false

[Fact] async Task Kills_process_after_timeout()
// RunAsync(new("powershell.exe", ["-NoProfile", "-Command", "Start-Sleep 30"], TimeSpan.FromSeconds(1)))
// → TimedOut == true, Duration < 10 s

[Fact] async Task Missing_executable_throws_FileNotFoundException()
// RunAsync(new("does-not-exist-fk.exe", [], TimeSpan.FromSeconds(5))) → throws FileNotFoundException
```

These three tests start real harmless processes. They are the only unit tests allowed to, and are marked `[Trait("Category", "Process")]`.

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/FormatKit.Core.Tests --filter Category=Process`
Expected: FAIL (types not implemented).

- [ ] **Step 5: Implement `ProcessRunner`**

`System.Diagnostics.Process` with `ArgumentList`, redirected stdout/stderr read asynchronously, `UseShellExecute=false`, `CreateNoWindow=true`. On timeout `Kill(entireProcessTree: true)`. Map `Win32Exception` with `NativeErrorCode == 2` to `FileNotFoundException`. Log command line, exit code, duration and output at Debug, and a one-line summary at Information.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/FormatKit.Core.Tests --filter Category=Process`
Expected: 3 passed.

- [ ] **Step 7: Add CI workflow**

`.github/workflows/ci.yml`: on push and pull_request, `runs-on: windows-latest`, `actions/setup-dotnet` with `dotnet-version: 10.0.x`, then `dotnet build -c Release` and `dotnet test -c Release --filter "Category!=Integration"`.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "chore: scaffold solution and add process runner"
```

---

### Task 2: Profile loading and validation

**Files:**
- Create: `src/FormatKit.Core/Profiles/Profile.cs`, `ProfileError.cs`, `ProfileLoader.cs`, `IModule.cs`
- Create: `src/FormatKit.Core/Localization/Strings.cs`, `src/FormatKit.Core/Resources/Strings.resx`, `Strings.pt-BR.resx`
- Create: `tests/FormatKit.Core.Tests/Fakes/FakeModule.cs`
- Test: `tests/FormatKit.Core.Tests/Profiles/ProfileLoaderTests.cs`, `tests/FormatKit.Core.Tests/Localization/StringsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `record Profile(int SchemaVersion, string Name, IReadOnlyDictionary<string, JsonElement> Modules)`
  - `record LoadedProfile(Profile Profile, string Path, string Hash)` — `Hash` is lowercase hex SHA-256 of the file bytes.
  - `enum ProfileErrorCode { FileNotFound, InvalidJson, MissingField, InvalidValue, UnsupportedSchemaVersion, UnknownModule }`
  - `record ProfileError(ProfileErrorCode Code, string Path, string Message)` — `Path` is a JSON path like `modules.drivers.sources[2]`; `Message` is localized.
  - `record ProfileLoadResult(LoadedProfile? Value, IReadOnlyList<ProfileError> Errors) { bool IsValid => Errors.Count == 0; }`
  - `interface IModule { string Name { get; } IReadOnlyList<ProfileError> Validate(JsonElement section, string path); bool IsEnabled(JsonElement section); IReadOnlyList<IStep> CreateSteps(JsonElement section); }` (`IStep` arrives in Task 3; declare `IModule` there if you prefer, but keep this exact shape)
  - `class ProfileLoader(IReadOnlyList<IModule> modules) { const int CurrentSchemaVersion = 1; ProfileLoadResult Load(string path); ProfileLoadResult Parse(string json, string path); }`
  - `static class Strings { string Get(string name); string Format(string name, params object[] args); }` — `internal` resource lookup per assembly; missing key returns the key.
  - `class FakeModule(string name) : IModule` — configurable errors and steps.

- [ ] **Step 1: Write failing tests**

```csharp
Valid_profile_loads()                    // {"schemaVersion":1,"name":"PC","modules":{"fake":{"enabled":true}}} → IsValid, Name=="PC", Hash.Length==64
Missing_file_reports_FileNotFound()      // Errors[0].Code==FileNotFound, Path==""
Invalid_json_reports_InvalidJson()       // "{ not json" → Code==InvalidJson
Missing_schemaVersion_reports_MissingField()  // Path=="schemaVersion"
Missing_name_reports_MissingField()      // Path=="name"
Wrong_type_reports_InvalidValue()        // "schemaVersion":"1" → Code==InvalidValue, Path=="schemaVersion"
Newer_schema_is_rejected()               // schemaVersion 2 → UnsupportedSchemaVersion
Unknown_module_is_rejected()             // modules.other → UnknownModule, Path=="modules.other"
Module_errors_are_collected_with_paths() // FakeModule returns error at "modules.fake.x" → present in Errors, Value==null
All_errors_reported_together()           // missing name AND unknown module → 2 errors
Hash_changes_when_file_changes()
```

`StringsTests`: with `CultureInfo.CurrentUICulture = new("pt-BR")`, `Strings.Get("ProfileError_FileNotFound")` returns the Portuguese text; with `en-US` returns English; `Strings.Get("NoSuchKey") == "NoSuchKey"`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FormatKit.Core.Tests --filter "FullyQualifiedName~Profiles|FullyQualifiedName~Localization"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`System.Text.Json` with `JsonDocument`; validate by hand to produce path-based errors (no exceptions escape `Load`). One resource key per `ProfileErrorCode` (`ProfileError_<Code>`), each with English and pt-BR text. `Strings` uses `ResourceManager("FormatKit.Core.Resources.Strings", typeof(Strings).Assembly)` and `CultureInfo.CurrentUICulture`.

- [ ] **Step 4: Run tests to verify they pass**

Same command. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): load and validate profiles"
```

---

### Task 3: Steps and plan building

**Files:**
- Create: `src/FormatKit.Core/Steps/IStep.cs`, `StepResult.cs`, `StepContext.cs`, `ServiceSet.cs`
- Create: `src/FormatKit.Core/Profiles/PlanBuilder.cs`, `BuiltInProfiles.cs`
- Create: `tests/FormatKit.Core.Tests/Fakes/FakeStep.cs`
- Test: `tests/FormatKit.Core.Tests/Profiles/PlanBuilderTests.cs`

**Interfaces:**
- Consumes: `Profile`, `IModule` (Task 2).
- Produces:
  - `enum StepOutcome { Succeeded, Skipped, Failed, RebootRequired }`
  - `record StepResult(StepOutcome Outcome, IReadOnlyList<string> Messages, string? NextAction = null)` with factories `Success(params string[] messages)`, `Skip(params string[] messages)`, `Fail(string? nextAction, params string[] messages)`, `Reboot(params string[] messages)`.
  - `interface IStep { string Id; string Title; bool RequiresAdmin; bool IsCritical; Task<bool> CheckAsync(StepContext ctx, CancellationToken ct); Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct); }` — `Title` is already localized by the step (implements the spec's `TitleKey` intent without a cross-assembly key lookup).
  - `record ServiceSet(IProcessRunner Processes, IFileSystem Files, IHardwareInfo Hardware, IWindowsUpdateClient WindowsUpdate, INetworkProbe Network, WingetClient Winget, PnpUtil PnpUtil)` — these types are defined in Tasks 7, 8 and 10; create empty placeholder types with the same names and namespaces now (`interface IHardwareInfo {}`, `interface IWindowsUpdateClient {}`, `interface INetworkProbe {}`, `class WingetClient {}`, `class PnpUtil {}`) so this compiles, and fill them in their tasks.
  - `record StepContext(ServiceSet Services, Serilog.ILogger Log)`
  - `static class PlanBuilder { IReadOnlyList<IStep> Build(Profile profile, IReadOnlyList<IModule> modules); }` — modules in the order of the `modules` list, only those present in the profile and enabled; duplicate step `Id` throws `InvalidOperationException`.
  - `static class BuiltInProfiles { const string DriversOnlyPath = "builtin:drivers"; Profile DriversOnly; string DriversOnlyHash = "builtin-drivers-v1"; }` — `modules.drivers = {"enabled":true,"sources":["windowsUpdate","oem","gpu"]}`.
  - `class FakeStep(string id, …)` — configurable `CheckResult`, `ExecuteResult`, `Throws`, `RequiresAdmin`, `IsCritical`; counts `CheckCalls` and `ExecuteCalls`.

- [ ] **Step 1: Write failing tests**

```csharp
Steps_follow_module_registration_order()   // modules [a, b], profile lists b then a → a's steps first
Disabled_module_contributes_no_steps()
Module_absent_from_profile_contributes_no_steps()
Duplicate_step_ids_throw()
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/FormatKit.Core.Tests --filter FullyQualifiedName~PlanBuilder`
Expected: FAIL.

- [ ] **Step 3: Implement the types above**

- [ ] **Step 4: Run to verify pass**

Same command. Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add step model and plan builder"
```

---

### Task 4: Run state persistence

**Files:**
- Create: `src/FormatKit.Core/AppPaths.cs`, `src/FormatKit.Core/SystemAccess/IFileSystem.cs`, `FileSystem.cs`
- Create: `src/FormatKit.Core/Running/RunState.cs`, `RunStateStore.cs`
- Create: `tests/FormatKit.Core.Tests/Fakes/InMemoryFileSystem.cs`
- Test: `tests/FormatKit.Core.Tests/Running/RunStateStoreTests.cs`, `tests/FormatKit.Core.Tests/SystemAccess/FileSystemTests.cs`

**Interfaces:**
- Consumes: `StepOutcome` (Task 3).
- Produces:
  - `record AppPaths(string Root) { static AppPaths Default; string StateFile; string LogsDirectory; string ReportsDirectory; }` — `Default.Root` = `%ProgramData%\FormatKit`.
  - `interface IFileSystem { bool FileExists(string path); bool DirectoryExists(string path); string ReadAllText(string path); void WriteAllTextAtomic(string path, string contents); void Delete(string path); void CreateDirectory(string path); IEnumerable<string> EnumerateFiles(string dir, string pattern, bool recursive); }`
  - `class FileSystem : IFileSystem` — atomic write = write `<path>.tmp`, then `File.Move(tmp, path, overwrite: true)`.
  - `record StepRecord(StepOutcome Outcome, DateTimeOffset FinishedAt, IReadOnlyList<string> Messages, string? NextAction)`
  - `record RunState(string ProfilePath, string ProfileHash, IReadOnlyList<string> PlanStepIds, Dictionary<string, StepRecord> Results, DateTimeOffset StartedAt)`
  - `enum StateLoadStatus { None, Loaded, Corrupt }`
  - `record StateLoadResult(StateLoadStatus Status, RunState? State, string? Error)`
  - `class RunStateStore(IFileSystem files, AppPaths paths) { StateLoadResult Load(); void Save(RunState state); void Delete(); }`

- [ ] **Step 1: Write failing tests**

```csharp
// RunStateStoreTests (InMemoryFileSystem)
Load_without_file_returns_None()
Save_then_Load_round_trips()                  // all fields equal, outcomes as strings in JSON
Corrupt_json_returns_Corrupt_with_message()   // file content "{\"profilePath\": " → Status==Corrupt, Error not empty, no throw
Wrong_shape_returns_Corrupt()                 // "[]" → Corrupt
Delete_removes_state()

// FileSystemTests (real temp directory, allowed)
WriteAllTextAtomic_replaces_existing_file_and_leaves_no_tmp()
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/FormatKit.Core.Tests --filter "FullyQualifiedName~RunStateStore|FullyQualifiedName~FileSystemTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`System.Text.Json` with `JsonStringEnumConverter` and camelCase. Catch `JsonException` and null deserialization to return `Corrupt`.

- [ ] **Step 4: Run to verify pass**

Same command. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): persist run state atomically"
```

---

### Task 5: Run engine

**Files:**
- Create: `src/FormatKit.Core/Running/RunEngine.cs`, `RunRequest.cs`, `RunSummary.cs`
- Create: `src/FormatKit.Core/SystemAccess/IScheduledTasks.cs`, `IRebooter.cs`
- Create: `tests/FormatKit.Core.Tests/Fakes/FakeScheduledTasks.cs`, `FakeRebooter.cs`, `FakeReportWriter.cs`
- Test: `tests/FormatKit.Core.Tests/Running/RunEngineTests.cs`

**Interfaces:**
- Consumes: `IStep`, `StepContext`, `StepResult` (Task 3); `RunStateStore`, `RunState`, `StepRecord` (Task 4).
- Produces:
  - `interface IScheduledTasks { Task RegisterResumeTaskAsync(CancellationToken ct); Task RemoveResumeTaskAsync(CancellationToken ct); }`
  - `interface IRebooter { Task<bool> RebootAsync(TimeSpan countdown, CancellationToken ct); }` — returns `false` when the user cancels.
  - `interface IReportWriter { string Write(RunSummary summary); }` — returns the report directory (implemented in Task 6).
  - `record RunRequest(string ProfilePath, string ProfileHash, IReadOnlyList<IStep> Plan, StepContext Context, bool Resume)`
  - `enum RunStatus { Completed, CompletedWithFailures, StoppedOnCriticalFailure, RebootPending, ProfileChanged, NothingToResume, StateCorrupt }`
  - `record StepReport(string Id, string Title, StepOutcome Outcome, IReadOnlyList<string> Messages, string? NextAction)`
  - `record RunSummary(RunStatus Status, IReadOnlyList<StepReport> Steps, string? ReportDirectory, string? Error)`
  - `class RunEngine(RunStateStore store, IScheduledTasks tasks, IRebooter rebooter, IReportWriter reports, TimeSpan rebootCountdown) { Task<RunSummary> RunAsync(RunRequest request, CancellationToken ct); }`

Engine algorithm (the tests pin each branch):

1. `Resume=false`: create a new `RunState` (overwriting any existing one) and save it. `Resume=true`: load state; `None` → `NothingToResume`; `Corrupt` → `StateCorrupt` with the store's error; hash differs from `request.ProfileHash` → `ProfileChanged`.
2. For each step in `Plan` without a record in `Results`:
   - `CheckAsync` true → record `Skipped`.
   - Otherwise `ExecuteAsync`; an exception other than `OperationCanceledException` becomes `Failed` with `ex.Message`.
   - Save state after every step.
   - `Failed` and `IsCritical` → status `StoppedOnCriticalFailure`; stop.
   - `RebootRequired` → `RegisterResumeTaskAsync`, then `RebootAsync(rebootCountdown)`; return `RebootPending` (state kept, report not written).
3. `OperationCanceledException` propagates; the interrupted step has no record.
4. On every terminal status except `RebootPending`, `NothingToResume`, `ProfileChanged` and `StateCorrupt`: `RemoveResumeTaskAsync`, write the report, delete state. Status is `CompletedWithFailures` if any step failed, else `Completed`.

- [ ] **Step 1: Write failing tests**

```csharp
Runs_all_steps_and_completes()                       // 2 steps succeed → Completed, both ExecuteCalls==1, state deleted, report written, RemoveResumeTask called
Step_already_done_is_skipped()                       // CheckResult=true → Outcome Skipped, ExecuteCalls==0
Non_critical_failure_continues()                     // step1 Fail, step2 runs → CompletedWithFailures
Critical_failure_stops_run()                         // step1 critical Fail → StoppedOnCriticalFailure, step2.ExecuteCalls==0, report written
Exception_becomes_failed_step()                      // Throws InvalidOperationException("boom") → Failed, Messages contains "boom", next step runs
Critical_step_exception_stops_run()
Reboot_registers_task_and_reboots()                  // RebootRequired → RegisterResumeTask called, RebootAsync(60 s) called, RebootPending, state still saved, report NOT written
Resume_continues_after_last_recorded_step()          // state has step1 Succeeded → only step2 executes
Resume_without_state_returns_NothingToResume()
Resume_with_corrupt_state_returns_StateCorrupt()
Resume_with_changed_profile_returns_ProfileChanged() // nothing executes
Cancellation_leaves_interrupted_step_unrecorded()    // step2 throws OperationCanceledException → propagates; saved state has step1 only
State_is_saved_after_each_step()                     // InMemoryFileSystem write count ≥ steps + 1
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/FormatKit.Core.Tests --filter FullyQualifiedName~RunEngine`
Expected: FAIL.

- [ ] **Step 3: Implement `RunEngine` as specified above**

- [ ] **Step 4: Run to verify pass**

Same command. Expected: 13 passed.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add resumable run engine"
```

---

### Task 6: Report writer and logging

**Files:**
- Create: `src/FormatKit.Core/Reporting/ReportWriter.cs`, `src/FormatKit.Core/Logging/LogSetup.cs`
- Modify: `src/FormatKit.Core/Resources/Strings.resx`, `Strings.pt-BR.resx` (report headings)
- Test: `tests/FormatKit.Core.Tests/Reporting/ReportWriterTests.cs`, `tests/FormatKit.Core.Tests/Logging/LogSetupTests.cs`

**Interfaces:**
- Consumes: `IReportWriter`, `RunSummary`, `StepReport` (Task 5); `IFileSystem`, `AppPaths` (Task 4).
- Produces:
  - `class ReportWriter(IFileSystem files, AppPaths paths, TimeProvider clock) : IReportWriter` — writes `reports\<yyyyMMdd-HHmmss>\report.json` and `report.md`.
  - `static class LogSetup { Serilog.ILogger Create(AppPaths paths, TimeProvider clock, bool verbose); }` — file sink at Debug to `logs\run-<yyyyMMdd-HHmmss>.log`; console sink at Information (Debug when `verbose`) with template `{Message:lj}{NewLine}{Exception}`.

- [ ] **Step 1: Write failing tests**

```csharp
Writes_json_and_markdown_in_timestamped_folder()   // FakeTimeProvider 2026-10-27 14:03:05 → folder "20261027-140305"
Json_contains_status_and_every_step()               // parse report.json: status=="completedWithFailures", steps.Length==summary.Steps.Count
Markdown_lists_failed_steps_with_next_action()      // contains step title and its NextAction text
Markdown_uses_current_ui_culture()                  // pt-BR → Portuguese heading key "Report_Title"
Log_file_is_created_in_logs_directory()             // real temp dir: write one entry, dispose, file "run-20261027-140305.log" exists
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/FormatKit.Core.Tests --filter "FullyQualifiedName~ReportWriter|FullyQualifiedName~LogSetup"`
Expected: FAIL.

- [ ] **Step 3: Implement**

Markdown layout: title, run status line, a table `Step | Result | Details`, then a "What to do next" section listing every `NextAction`. `FakeTimeProvider` comes from `Microsoft.Extensions.TimeProvider.Testing`.

- [ ] **Step 4: Run to verify pass**

Same command. Expected: 5 passed.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): write run reports and set up logging"
```

---

### Task 7: System adapters (scheduled task, reboot, elevation, network probe, winget, pnputil)

**Files:**
- Create in `src/FormatKit.Core/SystemAccess/`: `SchtasksScheduledTasks.cs`, `ConsoleRebooter.cs`, `IElevation.cs`, `Elevation.cs`, `INetworkProbe.cs`, `HttpNetworkProbe.cs`, `WingetClient.cs`, `PnpUtil.cs`
- Create: `tests/FormatKit.Core.Tests/Fakes/FakeNetworkProbe.cs`
- Test: `tests/FormatKit.Core.Tests/SystemAccess/SchtasksScheduledTasksTests.cs`, `WingetClientTests.cs`, `PnpUtilTests.cs`, `HttpNetworkProbeTests.cs`

**Interfaces:**
- Consumes: `IProcessRunner`, `ProcessSpec`, `ProcessResult`, `Timeouts` (Task 1); `IScheduledTasks`, `IRebooter` (Task 5).
- Produces:
  - `class SchtasksScheduledTasks(IProcessRunner runner, string exePath) : IScheduledTasks` — register: `schtasks.exe /Create /TN FormatKit\Resume /TR "\"<exe>\" run --resume" /SC ONLOGON /RL HIGHEST /F`; remove: `schtasks.exe /Delete /TN FormatKit\Resume /F`, where a "task does not exist" exit (non-zero with stderr containing `ERROR:`) is ignored on removal only. Non-zero exit on register throws `InvalidOperationException` with stderr.
  - `class ConsoleRebooter(IProcessRunner runner) : IRebooter` — prints remaining seconds every 10 s; Esc cancels and returns `false`; on expiry runs `shutdown.exe /r /t 0` and returns `true`. Verified manually in the VM (Task 15), no unit test.
  - `interface IElevation { bool IsElevated { get; } Task<int?> RelaunchElevatedAsync(IReadOnlyList<string> args); }` — `null` means the user declined UAC (Win32 error 1223). `Elevation` uses `WindowsPrincipal.IsInRole(Administrator)` and `Process.Start` with `Verb="runas"`, waiting for exit.
  - `interface INetworkProbe { Task<bool> IsOnlineAsync(CancellationToken ct); }` — `HttpNetworkProbe(HttpMessageHandler? handler = null)`: GET `http://www.msftconnecttest.com/connecttest.txt`, 10 s timeout, online when status 200 and body equals `Microsoft Connect Test`; any exception → `false`.
  - `enum WingetStatus { Installed, AlreadyInstalled, Failed, WingetMissing }`
  - `record WingetResult(WingetStatus Status, int ExitCode, string Output)`
  - `class WingetClient(IProcessRunner runner) { Task<WingetResult> InstallAsync(string id, CancellationToken ct); Task<bool> IsInstalledAsync(string id, CancellationToken ct); }`
    - install args: `install --id <id> --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity`, timeout `Timeouts.Install`
    - exit `0` → `Installed`; `-1978335189` (`0x8A15002B`) or `-1978335135` (`0x8A150061`) → `AlreadyInstalled`; other → `Failed`; `FileNotFoundException` → `WingetMissing`
    - list args: `list --id <id> --exact --accept-source-agreements --disable-interactivity`, timeout `Timeouts.Query`; exit `0` → `true`, otherwise `false`
  - `record PnpUtilResult(bool Success, bool RebootRequired, int ExitCode, string Output)`
  - `class PnpUtil(IProcessRunner runner) { Task<PnpUtilResult> ExportDriverAsync(string infName, string outputDir, CancellationToken ct); Task<PnpUtilResult> AddDriversAsync(string directory, CancellationToken ct); }`
    - export: `pnputil.exe /export-driver <inf> <dir>`; add: `pnputil.exe /add-driver <dir>\*.inf /subdirs /install`
    - exit `0` success; `3010` success with reboot required; other failure

- [ ] **Step 1: Write failing tests**

```csharp
// SchtasksScheduledTasksTests (FakeProcessRunner)
Register_uses_onlogon_highest_and_quoted_exe()     // exe "C:\\Tools\\formatkit.exe" → args contain "/TR", "\"C:\\Tools\\formatkit.exe\" run --resume", "/RL", "HIGHEST"
Register_failure_throws_with_stderr()
Remove_ignores_missing_task()

// WingetClientTests
Install_passes_silent_flags()                      // args exactly as specified
Install_maps_exit_codes(int exit, WingetStatus expected)  // [Theory] 0→Installed, -1978335189→AlreadyInstalled, -1978335135→AlreadyInstalled, 1→Failed
Missing_winget_maps_to_WingetMissing()
IsInstalled_true_on_exit_zero_false_otherwise()

// PnpUtilTests
Add_drivers_uses_subdirs_and_install()
Exit_3010_means_success_with_reboot()

// HttpNetworkProbeTests (stub HttpMessageHandler)
Online_when_body_matches()
Offline_when_body_differs()                        // captive portal page
Offline_on_exception()
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/FormatKit.Core.Tests --filter FullyQualifiedName~SystemAccess`
Expected: FAIL (new tests).

- [ ] **Step 3: Implement the adapters as specified**

- [ ] **Step 4: Run to verify pass**

Same command. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add system adapters for tasks, reboot, elevation, network, winget and pnputil"
```

---

### Task 8: Hardware detection

**Files:**
- Create: `src/FormatKit.Core/Hardware/HardwareSnapshot.cs`, `IHardwareInfo.cs`, `WmiHardwareInfo.cs`, `GpuVendors.cs`
- Create: `tests/FormatKit.Core.Tests/Fakes/FakeHardwareInfo.cs`
- Test: `tests/FormatKit.Core.Tests/Hardware/GpuVendorsTests.cs`, `tests/FormatKit.Core.Tests/Hardware/WmiHardwareInfoIntegrationTests.cs`

**Interfaces:**
- Produces:
  - `enum GpuVendor { Nvidia, Amd, Intel, Unknown }`
  - `record MachineInfo(string Manufacturer, string Model, string SystemFamily)`
  - `record BoardInfo(string Manufacturer, string Product)`
  - `record GpuInfo(string Name, string PnpDeviceId, GpuVendor Vendor, string? DriverVersion)`
  - `record ProblemDevice(string Name, string DeviceId, int ErrorCode, IReadOnlyList<string> HardwareIds)`
  - `record NetworkAdapterInfo(string Name, string PnpDeviceId, string? InfName, string? DriverVersion)`
  - `record HardwareSnapshot(MachineInfo Machine, BoardInfo Board, string BiosVersion, IReadOnlyList<GpuInfo> Gpus, IReadOnlyList<ProblemDevice> ProblemDevices, IReadOnlyList<NetworkAdapterInfo> NetworkAdapters)`
  - `interface IHardwareInfo { Task<HardwareSnapshot> ReadAsync(CancellationToken ct); }`
  - `class WmiHardwareInfo : IHardwareInfo` — queries per spec section 6.1; null WMI strings become `""`; `InfName`/`DriverVersion` come from `Win32_PnPSignedDriver` matched on `DeviceID`.
  - `static class GpuVendors { GpuVendor FromPnpId(string pnpDeviceId); }` — `VEN_10DE` Nvidia, `VEN_1002` Amd, `VEN_8086` Intel, case-insensitive; else Unknown.
  - `class FakeHardwareInfo(HardwareSnapshot snapshot) : IHardwareInfo` plus `static HardwareSnapshot Sample(...)` builder with optional overrides.

- [ ] **Step 1: Write failing tests**

```csharp
[Theory] FromPnpId_maps_vendor(string id, GpuVendor expected)
// "PCI\\VEN_10DE&DEV_2684" → Nvidia, "pci\\ven_1002&dev_744c" → Amd, "PCI\\VEN_8086&DEV_A780" → Intel, "ROOT\\BasicDisplay" → Unknown, "" → Unknown

[Fact, Trait("Category","Integration")] async Task Reads_real_machine()
// WmiHardwareInfo().ReadAsync → Machine.Manufacturer not null, NetworkAdapters not null, no exception
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/FormatKit.Core.Tests --filter FullyQualifiedName~Hardware`
Expected: FAIL.

- [ ] **Step 3: Implement**

- [ ] **Step 4: Run unit and integration tests**

Run: `dotnet test tests/FormatKit.Core.Tests --filter FullyQualifiedName~Hardware`
Expected: all pass on this PC (integration test is read-only and allowed locally; CI excludes it).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): detect hardware through WMI"
```

---

### Task 9: Vendor catalog and machine classification

**Files:**
- Create: `src/FormatKit.Drivers/Catalog/VendorCatalog.cs`, `MachineClassifier.cs`, `Data/vendors.json` (embedded resource)
- Test: `tests/FormatKit.Drivers.Tests/Catalog/VendorCatalogTests.cs`, `MachineClassifierTests.cs`

**Interfaces:**
- Consumes: `HardwareSnapshot`, `GpuVendor` (Task 8).
- Produces:
  - `record OemTool(string Id, IReadOnlyList<string> ManufacturerMatches, string WingetId, string Executable, IReadOnlyList<string> Arguments, IReadOnlyList<int> SuccessExitCodes, IReadOnlyList<int> RebootExitCodes)` — `Executable` may contain environment variables, expanded with `Environment.ExpandEnvironmentVariables` at use.
  - `record GpuPackage(GpuVendor Vendor, string WingetId)`
  - `record BoardSupport(IReadOnlyList<string> ManufacturerMatches, string SupportUrl)`
  - `record VendorCatalog(IReadOnlyList<OemTool> Oem, IReadOnlyList<GpuPackage> Gpu, IReadOnlyList<BoardSupport> Boards) { static VendorCatalog LoadEmbedded(); static VendorCatalog Parse(string json); }`
  - `record MachineKind(OemTool? Oem, BoardSupport? Board) { bool IsOem => Oem is not null; }`
  - `static class MachineClassifier { MachineKind Classify(HardwareSnapshot hw, VendorCatalog catalog); }` — a match is a case-insensitive "starts with" on the trimmed manufacturer. OEM matched on `Machine.Manufacturer`; when no OEM matches, board matched on `Board.Manufacturer`.

Initial `vendors.json` content (Step 5 verifies each ID):

| Kind | Match | Values |
|---|---|---|
| OEM `dell` | `Dell` | winget `Dell.CommandUpdate`; exe `%ProgramFiles%\Dell\CommandUpdate\dcu-cli.exe`; args `/applyUpdates -silent -reboot=disable`; success `0, 500`; reboot `1` |
| OEM `lenovo` | `LENOVO` | winget `Lenovo.SystemUpdate`; exe `%ProgramFiles(x86)%\Lenovo\System Update\Tvsu.exe`; args `/CM -search A -action INSTALL -includerebootpackages 1,3,4 -noreboot -noicon -nolicense`; success `0`; reboot none |
| OEM `hp` | `HP`, `Hewlett-Packard` | winget `HP.ImageAssistant`; exe `%ProgramFiles%\HP\HPIA\HPImageAssistant.exe`; args `/Operation:Analyze /Action:Install /Selection:All /Silent /Noninteractive`; success `0, 256`; reboot `3010` |
| GPU | Nvidia | `Nvidia.App` |
| GPU | Amd | `AMD.Adrenalin` |
| GPU | Intel | `Intel.IntelDriverAndSupportAssistant` |
| Board | `ASUSTeK` | `https://www.asus.com/support/` |
| Board | `Micro-Star` | `https://www.msi.com/support` |
| Board | `Gigabyte` | `https://www.gigabyte.com/Support` |
| Board | `ASRock` | `https://www.asrock.com/support/index.asp` |

- [ ] **Step 1: Write failing tests**

```csharp
// VendorCatalogTests
Embedded_catalog_loads_and_has_three_oems()
Every_oem_has_wingetId_executable_and_success_code()
Every_gpu_vendor_except_unknown_has_a_package()

// MachineClassifierTests
Dell_is_oem()                          // Manufacturer "Dell Inc." → Oem.Id=="dell"
Matching_ignores_case_and_whitespace() // "  lenovo " → lenovo
Custom_build_matches_board()           // Manufacturer "To Be Filled By O.E.M.", Board "ASUSTeK COMPUTER INC." → !IsOem, Board.SupportUrl asus
Empty_manufacturer_is_custom()         // "" and board "" → Oem null, Board null
Virtual_machine_is_custom()            // "Microsoft Corporation", "Virtual Machine" → Oem null, Board null
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/FormatKit.Drivers.Tests --filter FullyQualifiedName~Catalog`
Expected: FAIL.

- [ ] **Step 3: Implement**

- [ ] **Step 4: Run to verify pass**

Same command. Expected: all pass.

- [ ] **Step 5: Verify winget IDs against the live repository**

For each winget ID in `vendors.json` run `winget show --id <id> --exact --accept-source-agreements`. Expected: each resolves to the vendor's official package. If one fails, run `winget search "<vendor tool name>"`, use the official publisher's ID, and update the file and tests. For each OEM tool, read the vendor's command-line documentation and confirm the arguments and exit codes; correct the table above if they differ.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(drivers): add vendor catalog and machine classification"
```

---

### Task 10: Windows Update drivers

**Files:**
- Create: `src/FormatKit.Core/WindowsUpdate/IWindowsUpdateClient.cs`, `ComWindowsUpdateClient.cs`, `WindowsUpdateErrors.cs`
- Create: `src/FormatKit.Drivers/Steps/WindowsUpdateDriversStep.cs`, `src/FormatKit.Drivers/Localization/Strings.cs`, `src/FormatKit.Drivers/Resources/Strings.resx`, `Strings.pt-BR.resx`
- Create: `tests/FormatKit.Core.Tests/Fakes/FakeWindowsUpdateClient.cs`, `tests/FormatKit.Core.Tests/Fakes/TestServices.cs`
- Test: `tests/FormatKit.Core.Tests/WindowsUpdate/WindowsUpdateErrorsTests.cs`, `tests/FormatKit.Drivers.Tests/Steps/WindowsUpdateDriversStepTests.cs`

**Interfaces:**
- Consumes: `IStep`, `StepContext`, `StepResult` (Task 3).
- Produces:
  - `record DriverUpdate(string UpdateId, string Title, string? DriverHardwareId)`
  - `record WindowsUpdateInstallResult(bool Succeeded, bool RebootRequired, int HResult, IReadOnlyList<string> FailedTitles)`
  - `interface IWindowsUpdateClient { Task<IReadOnlyList<DriverUpdate>> SearchDriversAsync(CancellationToken ct); Task<WindowsUpdateInstallResult> InstallAsync(IReadOnlyList<DriverUpdate> updates, CancellationToken ct); }`
  - `class ComWindowsUpdateClient : IWindowsUpdateClient` — `Microsoft.Update.Session` via `Type.GetTypeFromProgID` and `dynamic`; search criteria `IsInstalled=0 and Type='Driver'`; download then install the found updates; runs the COM calls on a dedicated STA thread; `COMException` becomes a failed result with its `HResult`.
  - `static class WindowsUpdateErrors { string Describe(int hresult); }` — localized text for `0x8024402C` (cannot reach Windows Update), `0x80070422` (Windows Update service is disabled), `0x8024001E` (service stopped); otherwise a generic message containing `0x{hresult:X8}`.
  - `static class TestServices { ServiceSet Create(FakeProcessRunner? processes = null, FakeHardwareInfo? hardware = null, FakeWindowsUpdateClient? wu = null, FakeNetworkProbe? network = null, InMemoryFileSystem? files = null); StepContext Context(ServiceSet services); }`
  - `class WindowsUpdateDriversStep : IStep` — `Id="drivers.windows-update"`, admin, not critical. Check: search returns none → `true`. Execute: install all found; `RebootRequired` → `StepResult.Reboot`; success → `Success` listing titles; failure → `Fail(Describe(hresult))` listing failed titles.

- [ ] **Step 1: Write failing tests**

```csharp
// WindowsUpdateErrorsTests
[Theory] Known_codes_have_specific_text(int hresult)   // the three codes → text does not contain "0x"
Unknown_code_includes_hex()                             // 0x80240FFF → contains "0x80240FFF"

// WindowsUpdateDriversStepTests
Check_true_when_no_driver_updates()
Installs_found_updates_and_succeeds()                   // 2 updates → Success, messages contain both titles
Reboot_required_returns_RebootRequired()
Failure_uses_described_hresult_as_next_action()         // HResult 0x8024402C → Outcome Failed, NextAction == WindowsUpdateErrors.Describe(...)
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter "FullyQualifiedName~WindowsUpdate"`
Expected: FAIL.

- [ ] **Step 3: Implement**

- [ ] **Step 4: Run to verify pass**

Same command. Expected: all pass.

- [ ] **Step 5: Read-only smoke check on this PC**

Add `[Fact, Trait("Category","Integration")] SearchDrivers_runs_on_real_machine()` in `tests/FormatKit.Core.Tests/WindowsUpdate/ComWindowsUpdateClientIntegrationTests.cs` that only calls `SearchDriversAsync`.
Run: `dotnet test tests/FormatKit.Core.Tests --filter "FullyQualifiedName~ComWindowsUpdateClientIntegration"`
Expected: PASS (zero or more updates listed; nothing installed).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(drivers): install drivers from Windows Update"
```

---

### Task 11: Network check and network driver backup

**Files:**
- Create: `src/FormatKit.Drivers/Steps/NetworkCheckStep.cs`, `src/FormatKit.Drivers/NetworkDriverBackup.cs`
- Test: `tests/FormatKit.Drivers.Tests/Steps/NetworkCheckStepTests.cs`, `tests/FormatKit.Drivers.Tests/NetworkDriverBackupTests.cs`

**Interfaces:**
- Consumes: `INetworkProbe`, `PnpUtil`, `IFileSystem` (Tasks 4, 7); `IHardwareInfo` (Task 8); `TestServices` (Task 10).
- Produces:
  - `class NetworkCheckStep(string? backupDirectory, TimeSpan retryDelay) : IStep` — `Id="drivers.network-check"`, admin, **critical**. Check: `IsOnlineAsync`. Execute:
    - `backupDirectory` null → `Fail` with next action telling the user to connect a cable or set `modules.drivers.networkDriverBackup`.
    - directory missing → `Fail` naming the directory.
    - else `AddDriversAsync(dir)`, then probe up to 3 times with `retryDelay` between attempts; online → `Success`; still offline → `Fail` with instructions.
  - `record BackupResult(IReadOnlyList<string> Exported, IReadOnlyList<string> SkippedInbox, IReadOnlyList<string> Failed)`
  - `class NetworkDriverBackup(ServiceSet services) { Task<BackupResult> ExportAsync(string outputDirectory, CancellationToken ct); }` — for each physical adapter: `InfName` starting with `oem` (case-insensitive) → `ExportDriverAsync(inf, <output>\<sanitized adapter name>)`; other INF (inbox driver) → `SkippedInbox`; export failure → `Failed`.

- [ ] **Step 1: Write failing tests**

```csharp
// NetworkCheckStepTests (retryDelay = TimeSpan.Zero)
Check_true_when_online()
No_backup_configured_fails_with_next_action()       // NextAction mentions "networkDriverBackup"
Missing_backup_directory_fails()
Installs_backup_and_succeeds_when_online_after()    // probe: false (check), then true → Success, pnputil add-driver called with dir
Still_offline_after_install_fails()                 // probe always false → Failed, 3 probes after install
Step_is_critical()

// NetworkDriverBackupTests
Exports_only_oem_infs()                             // adapters: oem12.inf + netwtw10.inf → Exported 1, SkippedInbox 1
Export_failure_is_reported()
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/FormatKit.Drivers.Tests --filter "FullyQualifiedName~NetworkCheck|FullyQualifiedName~NetworkDriverBackup"`
Expected: FAIL.

- [ ] **Step 3: Implement**

- [ ] **Step 4: Run to verify pass**

Same command. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(drivers): add network check step and network driver backup"
```

---

### Task 12: OEM tool, GPU and leftover report steps

**Files:**
- Create: `src/FormatKit.Drivers/Steps/OemToolStep.cs`, `GpuStep.cs`, `LeftoverReportStep.cs`
- Test: `tests/FormatKit.Drivers.Tests/Steps/OemToolStepTests.cs`, `GpuStepTests.cs`, `LeftoverReportStepTests.cs`

**Interfaces:**
- Consumes: `VendorCatalog`, `MachineClassifier`, `MachineKind` (Task 9); `WingetClient` (Task 7); `IHardwareInfo` (Task 8); `TestServices` (Task 10).
- Produces:
  - `class OemToolStep(VendorCatalog catalog) : IStep` — `Id="drivers.oem-tool"`, admin. Check: machine not OEM → `true`. Execute: `winget install <WingetId>` (`Installed` or `AlreadyInstalled` continue; `WingetMissing` → `Fail` with the App Installer next action; `Failed` → `Fail`), then run the expanded `Executable` with `Arguments` and `Timeouts.Install`; exit in `SuccessExitCodes` → `Success`; in `RebootExitCodes` → `Reboot`; other → `Fail` with the exit code.
  - `class GpuStep(VendorCatalog catalog) : IStep` — `Id="drivers.gpu"`, admin. Packages = distinct `GpuPackage` for detected GPUs with a known vendor. Check: no packages, or all `IsInstalledAsync` → `true`. Execute: install each missing package; any failure → `Fail` listing it, else `Success`.
  - `class LeftoverReportStep(VendorCatalog catalog) : IStep` — `Id="drivers.leftover-report"`, not admin. Check: `false`. Execute: re-read hardware; no problem devices → `Success("all devices have drivers")` (localized); otherwise `Success` with one message per device: `<Name> (code <ErrorCode>): <first hardware ID> — https://www.catalog.update.microsoft.com/Search.aspx?q=<url-encoded first hardware ID>`; for a custom build with a matched board, add the board's support URL.

- [ ] **Step 1: Write failing tests**

```csharp
// OemToolStepTests
Skips_custom_build()
Installs_tool_then_runs_it()                    // Dell: winget install Dell.CommandUpdate, then dcu-cli with "/applyUpdates"
Already_installed_tool_still_runs()
Tool_reboot_exit_code_returns_RebootRequired()  // Dell exit 1
Tool_success_exit_code_500_succeeds()
Unknown_exit_code_fails_with_code_in_message()
Winget_missing_fails_with_app_installer_hint()

// GpuStepTests
Skips_when_no_known_gpu()                       // only "ROOT\\BasicDisplay"
Skips_when_package_already_installed()
Installs_missing_package()                      // Nvidia GPU → winget install Nvidia.App
Hybrid_intel_and_nvidia_installs_both()

// LeftoverReportStepTests
No_problem_devices_reports_all_good()
Lists_device_with_catalog_link()                // HWID "PCI\\VEN_8086&DEV_A0F0" → message contains "Search.aspx?q=PCI%5CVEN_8086%26DEV_A0F0"
Custom_build_adds_board_support_url()
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/FormatKit.Drivers.Tests --filter "FullyQualifiedName~OemTool|FullyQualifiedName~GpuStep|FullyQualifiedName~LeftoverReport"`
Expected: FAIL.

- [ ] **Step 3: Implement**

- [ ] **Step 4: Run to verify pass**

Same command. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(drivers): add OEM tool, GPU and leftover report steps"
```

---

### Task 13: Drivers module and scanner

**Files:**
- Create: `src/FormatKit.Drivers/DriversOptions.cs`, `DriversModule.cs`, `DriverScanner.cs`
- Test: `tests/FormatKit.Drivers.Tests/DriversModuleTests.cs`, `DriverScannerTests.cs`

**Interfaces:**
- Consumes: `IModule`, `ProfileError`, `ProfileErrorCode` (Task 2); all drivers steps (Tasks 10–12); `MachineClassifier` (Task 9).
- Produces:
  - `enum DriverSource { WindowsUpdate, Oem, Gpu }` — JSON values `windowsUpdate`, `oem`, `gpu`.
  - `record DriversOptions(bool Enabled, IReadOnlyList<DriverSource> Sources, string? NetworkDriverBackup)`
  - `class DriversModule(VendorCatalog catalog, TimeSpan networkRetryDelay) : IModule` — `Name="drivers"`. Validation: `enabled` required boolean; `sources` required non-empty array of known values without duplicates; `networkDriverBackup` optional string; unknown properties → `InvalidValue`. Steps, always in this order: `drivers.network-check`, then `windows-update`, `oem-tool`, `gpu` for the listed sources (fixed order regardless of list order), then `drivers.leftover-report`.
  - `record DriverScanReport(HardwareSnapshot Hardware, MachineKind Machine, IReadOnlyList<DriverUpdate> AvailableUpdates, IReadOnlyList<GpuPackage> GpuPackages, bool Online)`
  - `class DriverScanner(ServiceSet services, VendorCatalog catalog) { Task<DriverScanReport> ScanAsync(CancellationToken ct); }` — read-only.

- [ ] **Step 1: Write failing tests**

```csharp
// DriversModuleTests
Valid_section_has_no_errors()
Missing_enabled_is_error()                         // Path "modules.drivers.enabled"
Unknown_source_is_error_with_index_path()          // ["windowsUpdate","xyz"] → Path "modules.drivers.sources[1]"
Empty_sources_is_error()
Duplicate_source_is_error()
Unknown_property_is_error()
Steps_follow_fixed_order()                         // sources ["gpu","windowsUpdate"] → network-check, windows-update, gpu, leftover-report
Network_and_leftover_steps_are_always_present()    // sources ["oem"]

// DriverScannerTests
Scan_reports_machine_updates_and_gpu_packages()    // Dell + Nvidia + 1 WU update → Machine.IsOem, 1 update, 1 GPU package
Scan_never_installs()                              // FakeWindowsUpdateClient.InstallCalls == 0, FakeProcessRunner.Calls has no "install"
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/FormatKit.Drivers.Tests --filter "FullyQualifiedName~DriversModule|FullyQualifiedName~DriverScanner"`
Expected: FAIL.

- [ ] **Step 3: Implement**

- [ ] **Step 4: Run to verify pass**

Same command. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(drivers): add drivers module and scanner"
```

---

### Task 14: Command-line interface

**Files:**
- Create: `src/FormatKit.Cli/Program.cs`, `CliApp.cs`, `ExitCodes.cs`, `Services.cs`, `Commands/HardwareCommand.cs`, `Commands/DriversCommands.cs`, `Commands/RunCommand.cs`
- Create: `profiles/example.json`
- Test: `tests/FormatKit.Cli.Tests/CliAppTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces:
  - `static class ExitCodes { Ok = 0; CompletedWithFailures = 1; InvalidInput = 2; StoppedOnCriticalFailure = 3; RebootPending = 4; ElevationDeclined = 5; NothingToResume = 6; }`
  - `record CliEnvironment(ServiceSet Services, IElevation Elevation, IScheduledTasks ScheduledTasks, IRebooter Rebooter, IReportWriter Reports, RunStateStore StateStore, Serilog.ILogger Log, TextWriter Out, TimeSpan RebootCountdown, TimeSpan NetworkRetryDelay)`
  - `static class Services { CliEnvironment CreateReal(bool verbose); }` — the only place that builds real adapters.
  - `class CliApp(CliEnvironment env) { Task<int> RunAsync(string[] args, CancellationToken ct); }` — System.CommandLine root with the commands from the spec:
    - `hardware` — prints the snapshot as aligned text.
    - `drivers scan` — prints `DriverScanReport`.
    - `drivers install [--dry-run]` — uses `BuiltInProfiles.DriversOnly`; `--dry-run` prints the plan (step IDs and titles) and exits `Ok` without elevation.
    - `drivers backup-network --output <dir>` — admin; prints the `BackupResult`.
    - `run --profile <file> [--dry-run]` and `run --resume` — mutually exclusive; `--resume` loads state, uses `BuiltInProfiles` when `ProfilePath == "builtin:drivers"`, else reloads the profile file.
    - Global `--verbose`.
  - Behavior shared by admin commands: when the plan or command requires admin and `!Elevation.IsElevated`, call `RelaunchElevatedAsync(args)`; `null` → print the localized "administrator permission was declined" message and return `ElevationDeclined`; otherwise return the child's exit code.
  - Status → exit code: `Completed`→0, `CompletedWithFailures`→1, `StoppedOnCriticalFailure`→3, `RebootPending`→4, `NothingToResume`/`StateCorrupt`/`ProfileChanged`→6 with the localized message (for `StateCorrupt`, include the store's error and suggest `run --profile`).
  - Ctrl+C cancels the `CancellationToken`; the process exits after the engine propagates cancellation, printing that `run --resume` continues the run.

`profiles/example.json`:

```json
{
  "schemaVersion": 1,
  "name": "Example",
  "modules": {
    "drivers": { "enabled": true, "sources": ["windowsUpdate", "oem", "gpu"] }
  }
}
```

- [ ] **Step 1: Write failing tests** (all with fakes and `StringWriter` output)

```csharp
Invalid_profile_prints_errors_and_returns_InvalidInput()   // missing file → exit 2, output contains the path
Dry_run_prints_plan_without_elevating()                     // run --profile example --dry-run → exit 0, output lists "drivers.network-check", Elevation.RelaunchCalls == 0
Declined_elevation_returns_ElevationDeclined()              // not elevated, relaunch returns null → exit 5, no state written
Elevated_run_maps_completed_to_zero()
Critical_failure_maps_to_three()
Resume_without_state_returns_six()
Resume_with_corrupt_state_suggests_new_run()                // exit 6, output contains "run --profile"
Profile_and_resume_together_is_invalid_input()              // exit 2
Example_profile_is_valid()                                  // loads profiles/example.json through ProfileLoader with DriversModule → IsValid
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/FormatKit.Cli.Tests`
Expected: FAIL.

- [ ] **Step 3: Implement**

- [ ] **Step 4: Run to verify pass**

Same command. Expected: all pass.

- [ ] **Step 5: Manual read-only check on this PC**

Run: `dotnet run --project src/FormatKit.Cli -- hardware` and `dotnet run --project src/FormatKit.Cli -- drivers scan` and `dotnet run --project src/FormatKit.Cli -- drivers install --dry-run`
Expected: real hardware printed, scan report printed, plan printed; nothing installed.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(cli): add hardware, drivers and run commands"
```

---

### Task 15: Publishing, release workflow and documentation

**Files:**
- Modify: `src/FormatKit.Cli/FormatKit.Cli.csproj` (publish properties)
- Create: `.github/workflows/release.yml`, `README.md`, `docs/testing.md`

**Interfaces:**
- Consumes: the finished CLI.

- [ ] **Step 1: Configure single-file publish**

In the Cli csproj: `PublishSingleFile=true`, `SelfContained=true`, `IncludeNativeLibrariesForSelfExtract=true`, `SatelliteResourceLanguages=en;pt-BR`, `DebugType=embedded`.

Run: `dotnet publish src/FormatKit.Cli -c Release -o artifacts`
Expected: `artifacts\formatkit.exe` exists, single file.

- [ ] **Step 2: Smoke-test the published executable**

Run: `artifacts\formatkit.exe hardware` on this PC (Windows display language pt-BR).
Expected: hardware printed with Portuguese labels, proving the satellite resources are bundled.

- [ ] **Step 3: Release workflow**

`.github/workflows/release.yml`: on tags `v*`, `windows-latest`, setup .NET 10, `dotnet test -c Release --filter "Category!=Integration"`, `dotnet publish src/FormatKit.Cli -c Release -o artifacts`, upload `artifacts/formatkit.exe` to the GitHub release with `softprops/action-gh-release@v2`.

- [ ] **Step 4: README**

English: what FormatKit is, the clean-install philosophy, current status (core + drivers only), commands, example profile, a bold warning that it changes the system and should be tried in a VM first, unsigned-binary SmartScreen note, MIT license. End with a short "Resumo em português" section.

- [ ] **Step 5: VM test script**

`docs/testing.md`: step-by-step Hyper-V script covering (1) fresh Windows 11 VM, (2) `drivers backup-network` on the host to a shared folder, (3) `run --profile` in the VM with network disabled to exercise the network fallback and critical stop, (4) a run that triggers `RebootRequired`, checking the countdown, Esc cancel, scheduled task creation and resume after logon, (5) report contents, (6) task removed at the end. Each item lists the expected result.

- [ ] **Step 6: Full verification**

Run: `dotnet build -c Release` then `dotnet test -c Release --filter "Category!=Integration"`
Expected: build with 0 warnings; all tests pass.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "chore: add single-file publish, release workflow and docs"
```
