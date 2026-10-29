# FormatKit

Windows CLI (.NET 10, single self-contained `formatkit.exe`) that rebuilds a machine after a clean Windows install from a JSON profile. See README.md for features and docs/testing.md for the VM test script.

## Commands

- Build: `dotnet build`
- Unit tests (fast, run before every commit): `dotnet test -- --filter-not-trait "Category=Integration"`
- Publish: `dotnet publish src/FormatKit.Cli -c Release -o artifacts`
- Publish the UI: `dotnet publish src/FormatKit.App -c Release -o artifacts` (`FormatKitUI.exe`, next to `formatkit.exe`)

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable and implicit usings for every project: a warning breaks the build.

## Layout

- `src/FormatKit.Core`: engine (`Running/RunEngine`, run state, reboot and resume), profiles, steps (`IModule`, `IStep`, `StepResult`, `ServiceSet`), system access interfaces (`SystemAccess/`), logging, reporting.
- `src/FormatKit.Drivers`, `Cleanup`, `Programs`, `Dev`: one module each. `src/FormatKit.Cli`: System.CommandLine commands and wiring.
- `src/FormatKit.Hosting`: composition root and shared flows used by the CLI and the UI.
- `src/FormatKit.App`: WPF window plus the elevated worker mode (`--worker`), published as `FormatKitUI.exe`. Not `FormatKit.exe`: file names are case-insensitive and it would collide with `formatkit.exe`.
- `tests/FormatKit.<Project>.Tests`: xunit.v3, one test project per source project. Fakes for system access live in `tests/FormatKit.Core.Tests/Fakes`.
- `docs/superpowers/specs` and `plans`: the design spec and implementation plan of each sub-project. The spec is the authority; record what VM runs found in its "found while running" section.
- `tools/New-TestVm.ps1` creates the Hyper-V test VM; `tools/vm/` drives it (see below).

## Conventions

- Everything goes through the `SystemAccess` interfaces (registry, files, processes, services, console), so steps are unit-tested with fakes. No direct `Registry`, `File` or `Process` calls in steps.
- TDD: write the failing test first. Every fix found in a VM run gets a unit test that pins it.
- `ServiceSet`: add new services as the last parameter, then wire them in `src/FormatKit.Hosting/Services.cs` and `tests/FormatKit.Core.Tests/Fakes/TestServices.cs` (an optional parameter with a fake default).
- Dev module items are `DevItem` records with a `DevItemOutcome`; `DevStepResult.From` turns them into a `StepResult`.
- A step that is not done after a reboot returns `StepResult.RebootAndRunAgain` (it runs once more after the resume; a second request fails).
- PowerShell literals built in C# go through `PowerShellText.SingleQuote`.
- User-facing text lives in `Resources/Strings.resx` with a matching entry in `Strings.pt-BR.resx`; keep both in sync. Code, comments, docs and commit messages are in English.
- Never log or print secrets: passwords and SSH passphrases are typed by the user on an attached console; only public data is captured.
- Commits: conventional style (`feat(dev): ...`, `fix(core): ...`, `docs: ...`), ending with the `Co-Authored-By` trailer for the model that wrote the change.

## Safety

- FormatKit changes the system. Never run its system-changing commands on the host machine; run them only in the test VM.
- Tests and reviews never write to the host's registry, services or power settings, and never accept winget or Microsoft Store agreements on the host.
- VM credentials live only in the git-ignored `.vm/test-vm.json`. Never print them, paste them anywhere or commit them; read the file only from scripts.

## Test VM (`tools/vm/`)

Hyper-V VM made by `tools/New-TestVm.ps1`, driven over PowerShell Direct by `tools/vm/vm.ps1` (needs Hyper-V Administrators rights, not an elevated shell):

- `vm.ps1 restore` / `nested`: restore the `clean` checkpoint (`nested` also turns on nested virtualization, needed for WSL 2).
- `vm.ps1 copy -File <path>` copies into `C:\fk`; `vm.ps1 run -Command '<ps>'` runs there as the admin account, with no desktop.
- `vm.ps1 job -File <job.ps1> [-Visible] [-TimeoutSec N]` runs a script elevated on the signed-in user's desktop through a scheduled task. Use it for anything per-user or interactive. `-Visible` opens a window the user can watch and type into and returns at once; wait for `C:\fk\job.done`.
- `vmshot.ps1 [-Name x]` saves a console screenshot to `.vm\x.png`.

The VM blocks `.ps1` files (execution policy), so scripts in it run as `& ([scriptblock]::Create((Get-Content <file> -Raw)))`. For `job`, the user must be signed in and connected: a basic session in `vmconnect.exe localhost <vm name>` (a closed enhanced session leaves the user disconnected and the job never starts). Restoring `clean` detaches the extra data disk (`F:`) used by the Dev Drive scenario.
