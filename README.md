# FormatKit

FormatKit is a Windows command-line tool (.NET 10, a single `formatkit.exe`) that automates the tedious part of a clean Windows reinstall: after you format and install Windows, it detects your hardware and gets the machine to a fully working state with minimal clicking.

## Philosophy

A clean install beats a cloned or "migrated" one. FormatKit does not restore an old system image. It starts from a fresh Windows and rebuilds the working environment from a declarative **profile**: a small JSON file that says what to set up. Every step is logged and can be previewed with `--dry-run`.

## Status

Current release: **core + drivers + cleanup + programs + dev environment + preparation checklist + installation media**.

Implemented:

- Core engine: profiles, step planning, elevation, reboot handling and resume, run report.
- Drivers module: Windows Update drivers, OEM tool (Dell, HP, Lenovo), GPU driver, leftover-device report, network driver backup.
- Cleanup module: removes preinstalled apps and applies privacy and performance tweaks by level, with undo and a diagnosis of tweaks that break Windows.
- Programs module: installs your apps from a built-in catalog, free winget ids, a `winget export` file and direct installers with a mandatory hash check.
- Dev module: WSL with a distro and user, Git settings, an SSH key and ssh-agent, dotfiles, zsh and Oh My Zsh in WSL, Oh My Posh with a Nerd Font on Windows, and a Dev Drive with package caches. Windows developer settings (Developer Mode, long paths and others) are the `developer` cleanup category.

- Preparation checklist: `formatkit prepare check` lists what a reinstall would lose and what to save first (read-only, no elevation).

- Installation media: `formatkit media build` and `formatkit media usb` make a Windows 11 installation folder or USB stick from the official ISO that installs Windows with an answer file and starts your profile on the first logon. See [Make the installation media](#make-the-installation-media).

Planned (not available yet): WPF UI.

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
| `formatkit run --profile <file> [--dry-run] [--countdown <s>]` | Runs a profile; with `--dry-run` prints the plan without changing anything. `--countdown <s>` waits that many seconds first and Esc cancels (used on the first logon after the installation media; not valid with `--resume` or `--dry-run`). |
| `formatkit run --resume` | Resumes an interrupted run (used automatically after a reboot). |
| `formatkit cleanup list [--level <l>] [--category <c>]` | Lists the cleanup catalog (`--verbose` adds what each item does, what it breaks and the gain). |
| `formatkit cleanup check` | Diagnoses tweaks that already break or weaken this PC. Changes nothing, needs no elevation. |
| `formatkit cleanup undo <ids...> \| --all` | Reverts items FormatKit applied (asks for elevation). |
| `formatkit programs list [--category <c>] [--verbose]` | Lists the program catalog (`--verbose` adds the description and winget id). |
| `formatkit programs search <term>` | Runs `winget search` and prints its output as is. Exits 1 when winget finds nothing or is missing. |
| `formatkit programs export <file>` | Runs `winget export` on this PC and prints how to use the file as `importFile`. |
| `formatkit dev wsl-user [--profile <file>]` | Creates the WSL Linux user, lets you type its password and makes it the default user. Finishes an item a run left pending. |
| `formatkit dev ssh-key [--profile <file>]` | Creates the SSH key (you type the passphrase), starts ssh-agent and loads the key. Asks for elevation to set ssh-agent to Automatic. |
| `formatkit prepare check [--profile <file>] [--output <file.md>]` | Answers "is it safe to format?": lists what a reinstall would lose and what to save first. Changes nothing, needs no elevation. See [Before you format](#before-you-format). |
| `formatkit media build --iso <iso> --output <folder> [--profile <file>]` | Writes the Windows 11 installation media to a folder. See [Make the installation media](#make-the-installation-media). |
| `formatkit media usb --iso <iso> [--profile <file>] [--disk <n>] [--allow-hard-disk]` | Erases a USB stick you choose and writes the same media to it. Asks for elevation. |
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
  },
  // Used only by "formatkit media build" and "formatkit media usb"; "run" ignores it.
  "setup": {
    // "local" asks for a local account in Setup; "microsoft" keeps the online account screens. No password goes here.
    "account": "local"
    // "edition": "Windows 11 Pro",      // an edition in the ISO; omit to use the firmware key or Setup's edition screen
    // "computerName": "PC-JOAO",        // 1-15 letters, digits or '-'
    // "language": "pt-BR",              // must be a language of the ISO; default: your Windows language if the ISO has it
    // "keyboard": "0416:00010416",      // default: this PC's keyboard
    // "timeZone": "E. South America Standard Time"  // default: this PC's time zone
  }
}
```

Profiles may contain comments and trailing commas. The `setup` section is explained under [Make the installation media](#make-the-installation-media).

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

Levels are cumulative: `safe` is inside `balanced`, which is inside `aggressive`. Run `formatkit cleanup list --verbose` to see every item with what it does and what it can break. Items marked contested have no gain that Microsoft documents; they are always `aggressive`. Power plan items are skipped on laptops unless you list them in `include`. The `developer` category (Developer Mode, long paths, file extensions, hidden files, sudo, PowerShell execution policy) is never selected by a level; list its items in `include`, with or without `level`.

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

## Programs

The `programs` module installs your apps through winget. Modules run in this order: drivers, cleanup, programs, dev. `formatkit run --dry-run` lists the programs it would install.

Items come from four sources. If the same package appears more than once, it is installed once (catalog, then `winget`, then `importFile`; the first wins).

| Source | What it is |
| --- | --- |
| `catalog` | Ids from the built-in catalog (`formatkit programs list`), such as `browsers.chrome` or `games.steam`. |
| `winget` | Any winget package: `id`, plus optional `version`, `source` (`winget` or `msstore`) and `context` (`machine` or `user`). |
| `importFile` | A file written by `winget export` on your old PC (`formatkit programs export <file>`). |
| `direct` | Installers you download yourself: `name`, then `url` (https) or `path`, `sha256` and, for `.exe` only, `args`. |

Profile section:

```json
"programs": {
  "enabled": true,
  "acceptAgreements": true,
  "upgrade": false,
  "catalog": ["browsers.firefox", "communication.discord", "games.steam"],
  "winget": [
    { "id": "Notepad++.Notepad++" },
    { "id": "Python.Python.3.12", "version": "3.12.7" },
    { "id": "9NKSQGP7F2NH", "source": "msstore" },
    { "id": "Some.UserApp", "context": "user" }
  ],
  "importFile": "C:\\backup\\old-pc-programs.json",
  "direct": [
    { "name": "My app", "url": "https://example.com/setup.exe", "sha256": "<64 hex characters>", "args": ["/S"] },
    { "name": "Local tool", "path": "D:\\setups\\tool.msi" }
  ]
}
```

- `acceptAgreements` must be `true` when the section has any item. It means you accept the winget and Microsoft Store agreements for those packages; FormatKit passes the accept flags to winget on your behalf. Without it the profile is rejected.
- `upgrade`: when `true`, a program that is already installed is upgraded; when `false` (the default) it is left alone. A step whose programs are all installed is skipped with "Nothing to do: already in place."; a program that is checked inside a step that still runs is reported as "already installed".
- `version` pins a winget package to that exact version. It must match `[A-Za-z0-9._+-]+`, like ids and sources.
- `context: user` runs the install as the signed-in Windows user (not elevated) through a temporary scheduled task, for apps that install per user (the catalog marks Discord, Telegram, Zoom, Slack, Spotify, Playnite, Notion and JetBrains Toolbox this way). Catalog entries also carry a winget `scope` so winget picks the right installer; free winget ids and imported ones use winget's default. If nobody is signed in, the item fails ("failed: no user is signed in") and the next action is "Sign in to Windows and run again."
- A relative `importFile` or direct `path` is relative to the folder of the profile file, not to the current directory, so `"importFile": "old-pc.json"` next to the profile on a USB stick still works after a reboot resumes the run. Absolute paths are used as written.
- Unknown properties in the section, unknown catalog ids and an `importFile` that is missing or not a `winget export` JSON are profile errors (exit code 2).
- One failed program does not stop the others; the run ends with exit code 1 and the report lists what failed.

### Direct installers

A `direct` item is downloaded to `%ProgramData%\FormatKit\downloads` and its SHA-256 is checked **before anything runs**. The hash is mandatory for a `url`, because an https download only proves where the file came from, not that it is the file you meant: a hijacked mirror or a replaced release would otherwise run as administrator. A wrong hash fails the item and runs nothing; a downloaded (`url`) file is deleted, a local `path` file is never deleted. A `path` may carry a `sha256` too and is checked the same way. FormatKit keeps the file open, so it cannot be changed or replaced, from the hash check until the installer exits. Once an installer has started, Ctrl+C does not stop it: FormatKit stops after the current program.

`.msi` files run as `msiexec /i ... /qn /norestart` and ignore `args` (setting `args` on a `.msi` is an error). `.exe` files run with your `args`, passed as an argument list, never through a shell. Exit code 3010 or 1641 means a reboot is needed, and FormatKit handles it like any other step. Finished direct items are recorded in `%ProgramData%\FormatKit\programs-direct.json`, so a resumed run skips them. Other extensions are errors.

A ready profile is in `profiles/programs-example.json`. Preview it with `formatkit run --profile profiles\programs-example.json --dry-run`.

```powershell
formatkit programs list --category browsers
formatkit programs search "visual studio code"
formatkit programs export C:\backup\old-pc-programs.json
```

None of the three commands needs elevation.

### What FormatKit never installs

The catalog only lists official packages with a reliable silent install. It leaves out optimizers and "cleaners", driver updaters, third-party antivirus (Windows Defender is enough), installers that bundle adware and anything that needs a crack. Edge and Windows Terminal ship with Windows 11. Riot Client is not in the catalog because winget has no single package for it; add the game launcher you need as a `winget` or `direct` item. AMD and Intel GPU software stays out for the reason given under Driver sources.

## Dev environment

The `dev` module sets up a development machine after the `programs` module has installed the tools. It never installs programs itself: a missing Git or Oh My Posh fails with a hint to add `development.git` or `development.oh-my-posh` to `programs`. Every subsection is optional (a missing one adds no step) and every step can be run again: what is already in order is reported as such. One failed item does not stop the others.

Steps run in this order: `dev.wsl`, `dev.git`, `dev.ssh`, `dev.dotfiles`, `dev.shell`, `dev.devdrive`. A ready profile is in `profiles/dev-example.json`; preview it with `formatkit run --profile profiles\dev-example.json --dry-run`.

Profile section:

```json
"dev": {
  "enabled": true,
  "wsl": {
    "distro": "Ubuntu",
    "user": "joao",
    "wslconfig": { "memory": "8GB", "processors": 4, "swap": "2GB" }
  },
  "git": {
    "name": "Joao Silva",
    "email": "joao@example.com",
    "defaultBranch": "main",
    "editor": "code --wait",
    "autocrlf": "input"
  },
  "ssh": { "keyType": "ed25519", "comment": "joao@new-pc" },
  "dotfiles": {
    "repo": "https://github.com/joao/dotfiles.git",
    "cloneTo": "%USERPROFILE%\\dotfiles",
    "files": [
      { "from": "powershell/profile.ps1", "to": "%USERPROFILE%\\Documents\\PowerShell\\dotfiles.ps1" },
      { "from": "git/.gitconfig.local", "to": "%USERPROFILE%\\.gitconfig.local" }
    ]
  },
  "shell": {
    "wsl": { "zsh": true, "ohMyZsh": true, "theme": "robbyrussell",
             "plugins": ["git", "zsh-autosuggestions", "zsh-syntax-highlighting"] },
    "windows": { "ohMyPosh": true, "theme": "paradox", "font": "Meslo" }
  },
  "devDrive": {
    "path": "C:\\DevDrive\\dev.vhdx",
    "sizeGB": 100,
    "letter": "D",
    "label": "Dev",
    "caches": ["npm", "nuget", "pip", "cargo"]
  }
}
```

Validation errors carry the JSON path and fail the profile (exit code 2) before anything runs:

- `wsl.distro`: `Ubuntu` (default), `Ubuntu-24.04`, `Ubuntu-22.04` or `Debian`. `wsl.user` is required, matches `^[a-z_][a-z0-9_-]{0,31}$` and is not `root`. In `wslconfig`, `memory` and `swap` look like `8GB` or `512MB` and `processors` is from 1 to the PC's logical processor count.
- `git`: at least one key. `defaultBranch` matches `^[A-Za-z0-9._/-]+$`; `autocrlf` is `true`, `false` or `input`; `email` contains `@`.
- `ssh.keyType`: `ed25519` (default) or `rsa` (4096 bits). `comment` defaults to `<user>@<computer name>`.
- `dotfiles`: exactly one of `repo` (`https://...` or `git@host:path`, no credentials in the address) or `path` (a folder; a relative path is relative to the profile file). `files[].from` is relative with no `..`; `files[].to` must resolve inside your user profile and cannot be a file another dev step writes: `%USERPROFILE%\.gitconfig` when `git` or `ssh` is present, `%USERPROFILE%\.wslconfig` when `wsl.wslconfig` is present, and the two `Microsoft.PowerShell_profile.ps1` files (PowerShell 7 and Windows PowerShell) when `shell.windows.ohMyPosh` is true. Otherwise the dotfile and the step would overwrite each other on every run. At least one file.
- `shell.wsl` requires `wsl`; `ohMyZsh` requires `zsh`. Plugins are Oh My Zsh's own plugin names plus the two external ones, `zsh-autosuggestions` and `zsh-syntax-highlighting`. `shell.windows.font` is one of `Meslo` (default), `CascadiaCode`, `FiraCode`, `JetBrainsMono` or `Hack`; theme and plugin names cannot start with `-` or be `.`/`..`, and repeated plugins are listed once.
- `devDrive.path` is absolute, ends in `.vhdx` and is on a local fixed drive; `sizeGB` is at least 50; `letter` is not `C`; `caches` items are `npm`, `nuget`, `pip` or `cargo`.

### Suggested shell setup

Copy-ready subsections. In WSL, zsh with Oh My Zsh and the two usual plugins (needs a `wsl` subsection):

```json
"shell": {
  "wsl": { "zsh": true, "ohMyZsh": true, "theme": "robbyrussell",
           "plugins": ["git", "zsh-autosuggestions", "zsh-syntax-highlighting"] }
}
```

On Windows, Oh My Posh with the `paradox` theme and the Meslo Nerd Font (add `development.oh-my-posh` to `programs`, and `developer.ps-execution-policy` to the cleanup `include` list so Windows PowerShell 5.1 is allowed to load your profile):

```json
"shell": {
  "windows": { "ohMyPosh": true, "theme": "paradox", "font": "Meslo" }
}
```

Oh My Posh is installed per user (an MSIX package). Windows PowerShell 5.1 only loads a profile when its execution policy allows scripts (it is Restricted on a clean Windows): without `developer.ps-execution-policy` or an equivalent policy, the Windows PowerShell profile item is reported as pending. FormatKit installs the font once (a family that is already installed is left alone; if Windows Terminal was open during the install, close and reopen it), adds a marked block to your PowerShell 7 and Windows PowerShell profiles (only when they do not already call `oh-my-posh init`), and writes a Windows Terminal fragment at `%LOCALAPPDATA%\Microsoft\Windows Terminal\Fragments\FormatKit\formatkit.json` that sets the font (`MesloLGM Nerd Font` for Meslo) on the Windows PowerShell and PowerShell 7 profiles. Your Windows Terminal `settings.json` is never edited. In WSL, a `~/.zshrc` that FormatKit wrote (first line `# Managed by FormatKit`) is rewritten on later runs; one you wrote yourself is never replaced.

### WSL, passwords and reboots

- After the WSL platform is installed a **reboot is always required**; the run resumes after it, like any other reboot.
- Passwords are typed live in the console: the Linux user's password (`passwd`) and the SSH key passphrase (`ssh-keygen`, `ssh-add`). FormatKit never sees them and never logs them. When nobody can type (input redirected or nobody signed in) the item stays **pending** and its next action names `formatkit dev wsl-user --profile <your-profile.json>` or `formatkit dev ssh-key --profile <your-profile.json>`; replace the placeholder with the profile file you ran, because without `--profile` the commands use defaults instead of your `distro`, `user` and `keyType`. A pending item makes the step fail, so the run exits with code 1 and the report lists it.
- WSL is registered per Windows user. Run FormatKit from the account that will use WSL, or the step fails instead of installing for the wrong account.
- Virtualization must be on in the firmware (and, in a VM, nested virtualization), or WSL 2 cannot start.

### Dotfiles and line endings

Dotfiles are copied, never linked. A target that differs is first renamed to `<target>.formatkit-<yyyyMMdd-HHmmss>.bak`; an identical one is left alone. Files are compared byte by byte as checked out, so a repository that Git checks out with CRLF (`core.autocrlf=true`) differs from an LF copy and is replaced, with a backup, on every run. Keep dotfiles as LF (for example a `.gitattributes` with `* text=auto eol=lf`) or set `git.autocrlf` to `input`. A private repository relies on Git Credential Manager prompting you.

### Dev Drive

`devDrive` creates a dynamic VHDX formatted as a Dev Drive (ReFS) and assigns it the letter. It never resizes partitions, and it needs Windows 11 build 22621.2338 or newer (older builds report "not applicable"). Windows does not re-attach the VHDX after a reboot, so FormatKit registers a SYSTEM scheduled task, `FormatKit-DevDrive`, that mounts it at startup with the same letter. The path must not contain invalid characters or be a bare file in a drive root (`C:\.vhdx`). For each cache it creates `<letter>:\packages\<name>` and sets the user variable (`npm_config_cache`, `NUGET_PACKAGES`, `PIP_CACHE_DIR`, `CARGO_HOME`) unless it already points elsewhere; existing cache contents are not moved.

### Developer settings (cleanup category)

Windows developer settings are items of the `developer` cleanup category (`formatkit cleanup list --category developer`). They are never selected by a level; list them in `include`. The `cleanup` module applies them, so they are journaled and `cleanup undo` reverts them.

| Id | Change |
| --- | --- |
| `developer.developer-mode` | Developer Mode on. |
| `developer.long-paths` | Win32 long paths on. |
| `developer.show-extensions` | Shows file name extensions in Explorer. |
| `developer.show-hidden` | Shows hidden files in Explorer. |
| `developer.sudo` | `sudo` for Windows in inline mode (not applicable before Windows 11 24H2, when `sudo.exe` is missing). |
| `developer.ps-execution-policy` | Windows PowerShell 5.1 execution policy `RemoteSigned`. |

```json
"cleanup": { "enabled": true, "level": "safe",
             "include": ["developer.developer-mode", "developer.long-paths"] }
```

### Finishing pending items

```powershell
formatkit dev wsl-user --profile profiles\dev-example.json
formatkit dev ssh-key --profile profiles\dev-example.json
```

Both read the profile's `dev.wsl` or `dev.ssh` section. Without `--profile` they use `Ubuntu` with your Windows user name in lowercase (exit code 2 if that is not a valid Linux user name) and `ed25519`. They need a console where you can type (exit code 2 otherwise). Exit code 0 means every item is done, already in order or not applicable, 1 that an item failed or is pending, 2 an invalid profile. `dev ssh-key` relaunches elevated (like `run`) because it sets ssh-agent to Automatic, and exits with code 5 when you decline the UAC prompt. The `dev` commands write no report.

## Before you format

Run `formatkit prepare check` on the **old** Windows, before the reinstall. It inspects the PC and lists what must still be saved or exported, with a plain action for each item. It only reads: it copies and exports nothing, never asks for elevation and is safe to run on any PC. It points to the commands that do the backups, `formatkit programs export` and `formatkit drivers backup-network`, instead of running them.

```powershell
formatkit prepare check
formatkit prepare check --profile profiles\my-profile.json --output E:\checklist.md
```

- `--profile <file>` also checks the files and folders the profile needs after the format: `programs.importFile`, `programs.direct[].path`, `drivers.networkDriverBackup` and `dev.dotfiles.path`, plus the profile file itself. A profile input that does not exist is also a Blocker. An invalid profile prints its errors and exits with code 2 before any check runs.
- `--output <file.md>` also writes the checklist as Markdown, with a `- [ ]` box before every Blocker and Attention item. Write it to another drive: if the file is on the Windows drive, the command warns that it will be erased (it is written anyway). An existing file is overwritten.

Example output (abridged):

```text
Blocker
  Developer:
    Repository C:\Users\joao\Documents\app has work that exists only on this PC
      1 uncommitted change. Branch main has 2 commits not pushed.
      Commit and push, or copy the folder C:\Users\joao\Documents\app to an external drive.

Attention
  Security and licenses:
    D: is encrypted with BitLocker
      Without the recovery key, D: does not open after the reinstall.
      Save the recovery key of D: (aka.ms/myrecoverykey, or run manage-bde -protectors -get D: in an administrator terminal) and keep it off this PC.
  Apps and FormatKit:
    The list of installed programs has not been exported
      Run formatkit programs export E:\programs.json and keep the file off the Windows drive.

Ok
  Security and licenses:
    Windows is activated with the key in the firmware

1 item blocks formatting.
```

The last line sums up the run: `N items block formatting.` when there is a Blocker; otherwise `No blockers. Review the N items marked Attention before formatting.` when there are Attention items; and `Safe to format.` only when there is neither.

| Status | Meaning |
| --- | --- |
| Blocker | FormatKit can prove something is lost: for example a Git repository with uncommitted or unpushed work, or a file the profile needs that is on the Windows drive. Fix it before formatting. |
| Attention | Something may be lost or needs a step only you can confirm, such as saving a BitLocker recovery key, exporting a WSL distribution or backing up a user folder. |
| Not checked | The check could not run (for example it needs an administrator terminal, or Git is missing). The item gives the command to check by hand. |
| Ok | Nothing to do. |

"Lost" means the path is on the Windows drive (`%SystemDrive%`) or on a volume whose virtual disk (VHDX) file is on the Windows drive, such as a Dev Drive at `C:\DevDrive\dev.vhdx` mounted as `D:`. UNC paths are never lost; `\\?\C:\...` counts as `C:`. A `subst` drive or a junction that points into the Windows drive is not resolved, so check those yourself. If Windows cannot list the volumes, the BitLocker, volume and Dev Drive checks report Not checked, and a path on another drive is reported Not checked instead of Ok (it could be on a virtual disk stored on the Windows drive). A Blocker is reported only for loss FormatKit can prove; what only you can confirm (a saved recovery key, for example) is an Attention item, which is why the summary asks you to review them.

The check only looks at the user folders (Desktop, Documents, Pictures, Videos, Music, Downloads), the Git repositories under your user profile and on lost virtual disks (including repositories nested in other repositories, submodules and worktrees), the other items listed below, and the files the profile needs. Other data on the Windows drive, such as a `C:\src` folder or a folder directly in your user profile, is not inspected: check it yourself. Files ignored by Git (such as `.env`) are not in the remote even when everything is pushed.

The checks, by group:

- **Security and licenses:** `security.bitlocker`, `security.activation`, `security.windows11-requirements`.
- **Personal data:** `data.user-folders`, `data.other-users`, `data.volumes`.
- **Developer:** `dev.git-repos`, `dev.wsl`, `dev.keys`, `dev.devdrive`.
- **Apps and FormatKit:** `apps.programs-export`, `apps.network-drivers`, `apps.profile-inputs`, `apps.browsers`.

`security.windows11-requirements` matters on Windows 10 only: it warns when this PC would not install Windows 11 as is (RAM below about 4 GB, not UEFI, Secure Boot off, no TPM 2.0), because the installation media does not bypass those requirements. FormatKit keeps no list of supported processors, so every warning points you to the PC Health Check app for the processor. The TPM version is read only in an administrator terminal; otherwise (or when it cannot be read) the item is Not checked and suggests `tpm.msc`. On Windows 11 it is Ok.

In an elevated terminal the command also reads the BitLocker recovery protectors; otherwise it reports them as Not checked with the `manage-bde` command to run. It never elevates by itself.

| Exit code | Meaning |
| --- | --- |
| 0 | No Blocker. |
| 1 | At least one Blocker. |
| 2 | Invalid arguments, invalid profile or failed `--output` write (the report is printed first). |
| 130 | Cancelled with Ctrl+C. |

**Privacy:** the output and the log never contain Git remote URLs (only whether a remote exists), Windows product keys, BitLocker recovery keys or passwords. Only names, paths, sizes and counts are printed.

**Known limitation:** on a PC whose Windows Storage provider is broken, a Dev Drive VHDX is not detected (disk numbers still come from a Win32 fallback), so `dev.devdrive` does not list it. Check any virtual disk stored on `C:` by hand.

## Make the installation media

After [Before you format](#before-you-format), FormatKit can make the stick you boot from. It builds an installation folder or USB stick from the **official Windows 11 x64 ISO** (you download it from Microsoft; FormatKit does not). Windows installs with an answer file, and after the first logon your profile runs by itself from `C:\FormatKit`. It covers one case: a UEFI Windows 11 x64 PC, using only tools that ship with Windows.

```powershell
formatkit media build --iso C:\iso\Win11.iso --profile E:\my-profile.json --output D:\media
formatkit media usb   --iso C:\iso\Win11.iso --profile E:\my-profile.json            # lists the USB disks
formatkit media usb   --iso C:\iso\Win11.iso --profile E:\my-profile.json --disk 3    # erases disk 3 after you type 3
```

Both commands ask for elevation first (UAC), so the work and the confirmation run in the elevated window, which waits for Enter at the end. Without `--profile` you get a plain Windows 11 stick that runs nothing on the first logon. `media build` needs an `--output` folder that does not exist or is empty, on any drive; it checks the free space first. Run `formatkit prepare check` on the target PC before you format it.

### What the media contains

- The files of the ISO, with `autounattend.xml` at the root.
- `sources\install.wim` copied as is, or split into 3800 MB `install*.swm` parts when it is larger than 4 GiB minus 1 byte (FAT32 limit). An `install.esd` (Media Creation Tool ISOs) that large cannot be split and is refused.
- With `--profile`, under `sources\$OEM$\$1\FormatKit` (Setup copies it to `C:\FormatKit`): `formatkit.exe`, `profile.json` and `inputs\`, a copy of every file and folder the profile reads (`programs.importFile`, `programs.direct[].path`, `drivers.networkDriverBackup`, `dev.dotfiles.path`). Paths in the copied profile point at `inputs\<n>-<name>`, so the run never depends on the stick. Symbolic links and junctions inside a copied folder are skipped. A missing or unreadable input is an error (exit code 2) and nothing is written.

The ISO must be Windows 11 x64 (a Windows 10 or ARM64 ISO is refused). Setup is not told which disk to use and never gets a password.

### Screens you still answer

- The **disk** screen: choosing where Windows goes is the one decision that can destroy data, so you make it.
- The **local account** screen (name and password), unless `setup.account` is `microsoft`, which keeps the online account screens.
- The **edition** screen, unless the PC has a firmware key or the profile sets `setup.edition`.

Everything else (language, keyboard, time zone, license terms, privacy choices) comes from the answer file.

### The `setup` section

Optional, at the top level of the profile next to `modules`. `run` ignores it; only `media` reads it.

| Field | Rule | Default |
| --- | --- | --- |
| `account` | `local` or `microsoft`. | `local` |
| `edition` | An image name in the ISO, such as `Windows 11 Pro`; Setup gets Microsoft's public generic installation key for it, which installs but does not activate. | none: the firmware key picks the edition, or Setup asks |
| `computerName` | 1-15 letters, digits or `-`, not only digits. | none: Windows picks one |
| `language` | A culture name such as `pt-BR`; must be a language of the image (an English ISO has no Portuguese display language). | your Windows language if the image has it, else the image's default language |
| `keyboard` | An input locale (`0416:00010416`) or a culture name. | this PC's first keyboard layout |
| `timeZone` | A Windows time zone id. | this PC's time zone |

Unknown fields are errors, and there is no password field. A bad value or an edition or language the ISO does not have exits with code 2 before anything is written.

### First logon

With a profile, the first logon starts FormatKit in a visible console: "Starting the profile ... in 60 s (press Esc to cancel)...". Do nothing and the run proceeds (the UAC prompt appears only after the countdown, and reboots resume as usual). Esc prints the command to run later, waits for Enter and exits with code 0. Without a console the countdown is skipped. `C:\FormatKit` stays after the run; once it succeeds you can delete it.

### Writing a USB stick: `media usb`

`media usb` **erases the whole disk you choose**. It is built so it cannot erase one you did not clearly pick:

- Without `--disk` it only lists the USB disks (number, model, size, bus, drive letters and labels) and exits with code 2. Nothing is erased.
- It offers only removable USB sticks. External USB hard disks are hidden from the listing (it says how many) and need `--allow-hard-disk`. The boot and system disk are never offered.
- It refuses a disk that holds the ISO, the profile or any input (copy them elsewhere first), or that is too small.
- With `--disk <n>` it shows the disk and its volumes, then you must **type the disk number** at a console. Anything else cancels, and with no console it refuses. There is no `--yes`.
- It lists the disks again after you answer and erases only if the same disk (number and unique id) still passes every rule, so a stick swapped meanwhile is not touched.
- A failure or Ctrl+C after the erase starts reports an incomplete stick; run the command again to fix it. The erase itself cannot be undone.

The stick is MBR with one active FAT32 partition labeled `FORMATKIT`, up to 32 GiB (Windows cannot format larger FAT32 volumes), which UEFI firmware boots with Secure Boot on. The rest of a larger stick stays unallocated. **Ventoy and similar multi-boot tools are not supported**: Ventoy ignores `autounattend.xml` at the root of the media.

| Exit code | Meaning |
| --- | --- |
| 0 | Media written. |
| 1 | Failed while writing (delete a partial `--output` folder and run again; a stick needs the command again). |
| 2 | Invalid arguments, ISO, profile or `setup`; a missing input; not enough space; a disk that cannot be used; a refused or missing confirmation. |
| 5 | The UAC prompt was declined. |
| 130 | Cancelled with Ctrl+C. |

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

O FormatKit é uma ferramenta de linha de comando para Windows que automatiza a parte chata de uma reinstalação limpa: detecta o hardware e instala os drivers a partir de um perfil JSON. Estado atual: núcleo, drivers, limpeza do sistema, instalação de programas (winget, catálogo e instaladores diretos com verificação de hash), ambiente de desenvolvimento (WSL, Git, SSH, dotfiles, shell e Dev Drive) e checklist de preparação (`formatkit prepare check`, somente leitura: mostra o que a formatação apagaria e o que salvar antes). Mídia de instalação (`formatkit media build` e `formatkit media usb`: pendrive ou pasta de instalação do Windows 11 a partir da ISO oficial, com resposta automática e execução do perfil no primeiro logon) já existe; a interface WPF é planejada.

**Atenção: a ferramenta altera o sistema, instala drivers e pode reiniciar o computador. Teste primeiro em uma máquina virtual** e use `--dry-run`. O executável não é assinado, então o SmartScreen pode exibir um aviso. Os textos aparecem em português quando o Windows está em pt-BR. O `media usb` apaga o disco inteiro que você escolher, só depois de você digitar o número do disco. Códigos de saída: veja a tabela acima. Licença MIT.
