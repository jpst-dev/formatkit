# FormatKit

FormatKit is a Windows command-line tool (.NET 10, a single `formatkit.exe`) that automates the tedious part of a clean Windows reinstall: after you format and install Windows, it detects your hardware and gets the machine to a fully working state with minimal clicking.

## Philosophy

A clean install beats a cloned or "migrated" one. FormatKit does not restore an old system image. It starts from a fresh Windows and rebuilds the working environment from a declarative **profile**: a small JSON file that says what to set up. Every step is logged and can be previewed with `--dry-run`.

## Status

Current release: **core + drivers only**.

Implemented:

- Core engine: profiles, step planning, elevation, reboot handling and resume, run report.
- Drivers module: Windows Update drivers, OEM tool (Dell, HP, Lenovo), GPU driver, leftover-device report, network driver backup.

Planned (not available yet): system cleanup, apps and dev environment, installation media, preparation checklist, WPF UI.

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
| `--verbose` (global) | Shows detailed logging. |

Output language follows the Windows UI language (English and Brazilian Portuguese).

### Driver sources

- **Windows Update**: for all vendors. AMD GPUs get their drivers from Windows Update only; there is no AMD package.
- **OEM tool**: Dell, HP and Lenovo vendor tooling.
- **GPU**: Nvidia is installed through the Microsoft Store NVIDIA App (`msstore XP8CLZL93F5Z4P`); Intel through Intel Driver & Support Assistant (winget).

### Elevation, reboots and resume

- Steps that need administrator rights trigger a UAC prompt. The elevated run opens a **second window** that waits for **Enter** at the end so you can read the result (except when the run ends with exit code 4, reboot pending, where it closes by itself). The run resumed by the scheduled task after a reboot is not a UAC relaunch: it runs in the task's own console window, which closes when the process exits, so check the log or report for the result.
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
"drivers": { "enabled": true, "networkDriverBackup": "D:\\netdrivers" }
```

```powershell
formatkit run --profile profiles\example.json --dry-run
formatkit run --profile profiles\example.json
```

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

O FormatKit é uma ferramenta de linha de comando para Windows que automatiza a parte chata de uma reinstalação limpa: detecta o hardware e instala os drivers a partir de um perfil JSON. Estado atual: apenas núcleo e drivers (limpeza, apps, mídia de instalação, checklist e interface WPF são planejados).

**Atenção: a ferramenta altera o sistema, instala drivers e pode reiniciar o computador. Teste primeiro em uma máquina virtual** e use `--dry-run`. O executável não é assinado, então o SmartScreen pode exibir um aviso. Os textos aparecem em português quando o Windows está em pt-BR. Códigos de saída: veja a tabela acima. Licença MIT.
