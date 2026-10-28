# FormatKit

FormatKit is a Windows command-line tool (.NET 10, a single `formatkit.exe`) that automates the tedious part of a clean Windows reinstall: after you format and install Windows, it detects your hardware and gets the machine to a fully working state with minimal clicking.

## Philosophy

A clean install beats a cloned or "migrated" one. FormatKit does not restore an old system image. It starts from a fresh Windows and rebuilds the working environment from a declarative **profile**: a small JSON file that says what to set up. Every step is logged and can be previewed with `--dry-run`.

## Status

Current release: **core + drivers + cleanup**.

Implemented:

- Core engine: profiles, step planning, elevation, reboot handling and resume, run report.
- Drivers module: Windows Update drivers, OEM tool (Dell, HP, Lenovo), GPU driver, leftover-device report, network driver backup.
- Cleanup module: removes preinstalled apps and applies privacy and performance tweaks by level, with undo and a diagnosis of tweaks that break Windows.

Planned (not available yet): apps and dev environment, installation media, preparation checklist, WPF UI.

## **WARNING: this tool changes your system**

FormatKit installs drivers and software, runs vendor tools, needs administrator rights and **can reboot the computer**. **Try it in a virtual machine first** (see [docs/testing.md](docs/testing.md)) and use `--dry-run` before a real run. You use it at your own risk.

## Installation

Download `formatkit.exe` from the [Releases](../../releases) page. It is self-contained; no .NET install is needed. Requires Windows 11 x64.

The binary is **not code-signed**, so Windows SmartScreen may show "Windows protected your PC". Click **More info**, then **Run anyway** if you trust the source. You can also build it yourself (see below).

## Commands

| Command | What it does |
| --- | --- |
| `formatkit hardware` | Shows the detected hardware (machine, board, BIOS, GPUs, problem devices, network adapters). |
| `formatkit drivers scan` | Scans for devices missing drivers. |
| `formatkit drivers install [--dry-run]` | Installs drivers; with `--dry-run` only prints the plan. |
| `formatkit drivers backup-network --output <dir>` | Exports the working network drivers to a folder (do this before formatting). |
| `formatkit run --profile <file> [--dry-run]` | Runs a profile; with `--dry-run` prints the plan without changing anything. |
| `formatkit run --resume` | Resumes an interrupted run (used automatically after a reboot). |
| `formatkit cleanup list [--level <l>] [--category <c>]` | Lists the cleanup catalog (`--verbose` adds what each item does, what it breaks and the gain). |
| `formatkit cleanup check` | Diagnoses tweaks that already break or weaken this PC. Changes nothing, needs no elevation. |
| `formatkit cleanup undo <ids...> \| --all` | Reverts items FormatKit applied (asks for elevation). |
| `--verbose` (global) | Shows detailed logging. |

Output language follows the Windows UI language (English and Brazilian Portuguese).

### Driver sources

- **Windows Update**: for all vendors. AMD and Intel GPUs get their drivers from Windows Update (or the OEM tool) only; FormatKit installs no AMD or Intel GPU software, to avoid resident updaters.
- **OEM tool**: Dell, HP and Lenovo vendor tooling.
- **GPU**: Nvidia is installed through the Microsoft Store NVIDIA App (`msstore XP8CLZL93F5Z4P`).

### Elevation, reboots and resume

- Steps that need administrator rights trigger a UAC prompt. The elevated run opens a **second window** that waits for **Enter** at the end so you can read the result (except when the run ends with exit code 4, reboot pending, where it closes by itself). The run resumed by the scheduled task after a reboot also waits for **Enter** at the end (the task passes a hidden `--wait-on-exit` flag), again except when the exit code is 4, so you can read the result before the window closes.
- If a step needs a reboot, FormatKit shows a countdown and creates a scheduled task that resumes the run at the next logon. Pressing Esc **cancels the countdown**; the run is then left to resume at your next logon.
- If the network is down, the run falls back to a backed-up network driver; a critical failure stops the run.

### Exit codes

| Code | Name | Meaning |
| --- | --- | --- |
| 0 | Ok | Everything succeeded. |
| 1 | CompletedWithFailures | Finished, but some steps failed. |
| 2 | InvalidInput | Bad arguments or invalid profile. |
| 3 | StoppedOnCriticalFailure | A critical step failed and the run stopped. |
| 4 | RebootPending | A reboot is required to continue. |
| 5 | ElevationDeclined | The UAC prompt was declined. |
| 6 | NothingToResume | Nothing to resume: no interrupted run, corrupt saved state, or the profile changed or became invalid since the run started. |
| 130 | Cancelled | Cancelled with Ctrl+C. |

## Example profile

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

Optional `modules.drivers.networkDriverBackup`: path to a folder created by `drivers backup-network`. If the machine is offline, the network check step installs the drivers from that folder with `pnputil`. Without it, an offline run stops with exit code 3. In JSON, backslashes must be doubled:

```json
"drivers": { "enabled": true, "sources": ["windowsUpdate", "oem", "gpu"], "networkDriverBackup": "D:\\netdrivers" }
```

```powershell
formatkit run --profile profiles\example.json --dry-run
formatkit run --profile profiles\example.json
```

## Cleanup

The `cleanup` module removes preinstalled apps and applies privacy and performance tweaks. Every item has a level, and every change is recorded in a journal so it can be undone.

| Level | What it holds |
| --- | --- |
| `safe` | Removes the bundled apps almost nobody uses (Clipchamp, Solitaire, News, Weather, Feedback Hub, Power Automate), turns off advertising ID, tailored experiences, activity history and similar, and turns Game Mode on. |
| `balanced` | Everything in `safe`, plus more apps (Teams personal, Get Help, Xbox app, Phone Link, To Do), minimal diagnostic data, no web search in Start, online speech recognition off, the High performance power plan and pointer precision off. |
| `aggressive` | Everything in `balanced`, plus the Ultimate Performance power plan, Memory Integrity and VBS off (reduces security) and the contested tweaks. |

Levels are cumulative: `safe` is inside `balanced`, which is inside `aggressive`. Run `formatkit cleanup list --verbose` to see every item with what it does and what it can break. Items marked contested have no gain that Microsoft documents; they are always `aggressive`. Power plan items are skipped on laptops unless you list them in `include`.

Profile section:

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
- `include` and `exclude` take item ids from `cleanup list`. The selection is every item at or below `level`, plus `include`, minus `exclude`. An unknown id, or an id in both lists, is a profile error.
- `confirmAggressive` must be `true` when the selection contains any `aggressive` item (including one added through `include`). Without it the profile is rejected and the message names those items.

A ready profile is in `profiles/cleanup-balanced.json`. Preview it with `formatkit run --profile profiles\cleanup-balanced.json --dry-run`.

```powershell
formatkit cleanup list --level balanced
formatkit cleanup check
formatkit cleanup undo apps.clipchamp
formatkit cleanup undo --all
```

`cleanup check` exits with code 1 when it finds something under "Breaks Windows features" or "Security risks", and 0 otherwise. Some popular tweaks are deliberately left out; see [docs/cleanup-not-included.md](docs/cleanup-not-included.md) for why and how `cleanup check` reports each one.

## Building

```powershell
dotnet build -c Release
dotnet test -c Release -- --filter-not-trait "Category=Integration"
dotnet publish src/FormatKit.Cli -c Release -o artifacts   # artifacts\formatkit.exe
```

Pushing a tag like `v0.1.0` runs the release workflow, which tests, publishes and attaches `formatkit.exe` to a GitHub release.

## License

[MIT](LICENSE)

## Resumo em português

O FormatKit é uma ferramenta de linha de comando para Windows que automatiza a parte chata de uma reinstalação limpa: detecta o hardware e instala os drivers a partir de um perfil JSON. Estado atual: núcleo, drivers e limpeza do sistema (apps, mídia de instalação, checklist e interface WPF são planejados).

**Atenção: a ferramenta altera o sistema, instala drivers e pode reiniciar o computador. Teste primeiro em uma máquina virtual** e use `--dry-run`. O executável não é assinado, então o SmartScreen pode exibir um aviso. Os textos aparecem em português quando o Windows está em pt-BR. Códigos de saída: veja a tabela acima. Licença MIT.
