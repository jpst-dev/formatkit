# FormatKit — Dev Environment Design

- **Date:** 2026-10-28
- **Status:** Draft, awaiting review
- **Scope:** Sub-project 4b (dev environment): WSL, Git, SSH, dotfiles,
  Dev Drive, terminal shell setup, and Windows developer settings
- **Builds on:** `2026-10-27-core-and-drivers-design.md` (profile, step engine,
  resume, logs, report, exit codes), `2026-10-27-cleanup-design.md` (catalog,
  journal, undo, check) and `2026-10-28-programs-design.md` (program installs,
  user-context runner)

## 1. Purpose

After a clean install and the user's programs, leave a developer's machine
ready to work: WSL with a Linux user, Git configured, an SSH key ready for
GitHub, their dotfiles in place, a Dev Drive for code and package caches, and
a nicer terminal. Everything is opt-in; a profile without a `dev` section, or
a cleanup level without explicit developer ids, changes nothing here.

## 2. Scope

**In 4b:**

- A `dev` module run after `programs`, with steps `dev.wsl`, `dev.git`,
  `dev.ssh`, `dev.dotfiles`, `dev.shell`, `dev.devdrive`.
- A `developer` category in the cleanup catalog for Windows developer
  settings, selected only by explicit id.
- A `development.oh-my-posh` entry in the programs catalog.
- Commands `dev wsl-user` and `dev ssh-key` to finish items left pending.

**Out of scope:** installing languages and tools (the `programs` module does
it); registering the SSH key on GitHub (needs a user token); restoring an old
private SSH key; Linux packages other than `zsh`; Linux dotfiles; symlinked
dotfiles; resizing partitions for a Dev Drive; moving `%TEMP%` to the Dev
Drive; undo for the `dev` module (its changes are user files and settings the
user can edit; the developer cleanup items have undo through cleanup).

## 3. Decisions

| # | Decision | Reason |
|---|---|---|
| D1 | New module `dev` in project `FormatKit.Dev`; Windows developer settings are a cleanup category instead (approach A). | The settings are registry values; cleanup already has the journal, `cleanup undo` and `cleanup check`. |
| D2 | `developer` items are never selected by `level`, only by `include`. | A non-developer choosing `level: "safe"` must not get Developer Mode. |
| D3 | FormatKit never sees a password or passphrase. The user types them into `passwd`, `ssh-keygen` and `ssh-add` directly, on FormatKit's console. | Profiles travel on USB drives and GitHub; secrets never belong in them or in logs. |
| D4 | When nobody is at the machine, items needing a password end as **pending** with the command that finishes them (`formatkit dev wsl-user`, `formatkit dev ssh-key`). | Unattended runs still finish; nothing is left half-done silently. |
| D5 | Dotfiles are copied, never linked. A differing target is backed up first. | Works without Developer Mode and survives programs that rewrite their settings file. |
| D6 | Dev Drive only on a new dynamic VHDX; no partition resizing. | Resizing partitions is the one option that can lose data. |
| D7 | Package-cache variables are set for the user, not the machine, and an existing variable pointing elsewhere is left alone (warning). | No machine-wide change; never overrides a choice the user made. |
| D8 | The `dev` module never installs programs. A missing Git fails with "add `development.git` to programs". | The programs module stays the only place that accepts license agreements. |
| D9 | The Windows Terminal font is set through a FormatKit fragment, not by rewriting `settings.json`. | The user's settings file (often from dotfiles) is never touched. |
| D10 | One failed item never stops the others; each step runs its items in order and reports each one. | Same rule as programs. |

## 4. Architecture

New project `FormatKit.Dev`, referencing only `FormatKit.Core`. The module
order becomes drivers → cleanup → programs → dev.

| Unit | Responsibility |
|---|---|
| `DevOptions` | Parses and validates the `dev` section (section 5). |
| `DevModule` | `IModule` named `dev`: validation and step creation, one step per present subsection, in the order of section 6. |
| `Steps/WslStep` | `dev.wsl` (6.1). |
| `Steps/GitStep` | `dev.git` (6.2). |
| `Steps/SshStep` | `dev.ssh` (6.3). |
| `Steps/DotfilesStep` | `dev.dotfiles` (6.4). |
| `Steps/ShellStep` | `dev.shell` (6.5). |
| `Steps/DevDriveStep` | `dev.devdrive` (6.6). |
| `Core/SystemAccess/WslClient` | `wsl.exe` calls: status, installed distros (UTF-16 output), install, run a command in a distro as a user, with or without the console attached. |
| `Core/SystemAccess/GitLocator` | Finds `git.exe` on `PATH`, then in the default install locations (the process `PATH` predates a Git installed in this run). |
| `Core/SystemAccess/IInteractiveConsole` | Says whether someone can type (console input not redirected and an interactive user signed in), and runs a process with the console attached. |
| `Core/SystemAccess/VirtualDiskManager` | Creates, attaches and formats a VHDX as a Dev Drive (`diskpart` script plus `Format-Volume -DevDrive`), and reports whether a VHDX is attached and where. |
| `Core/SystemAccess/UserEnvironment` | Reads and writes user environment variables and broadcasts the change. |
| Cleanup catalog | New category `developer` (section 7). |
| Programs catalog | New entry `development.oh-my-posh` (`JanDeDobbeleer.OhMyPosh`). |
| CLI `Commands/DevCommands` | `dev wsl-user`, `dev ssh-key` (section 8). |

Every value placed on a command line goes through an argument list, never a
shell. Values that end up inside a Linux shell command (user name, theme,
plugin names) must match the patterns in section 5 and are passed as separate
arguments to `wsl.exe --exec`, so no shell parses them.

## 5. Profile

```json
"dev": {
  "enabled": true,
  "wsl": {
    "distro": "Ubuntu",
    "user": "joao",
    "wslconfig": { "memory": "8GB", "processors": 4, "swap": "2GB" }
  },
  "git": {
    "name": "João Silva",
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
      { "from": "powershell/profile.ps1", "to": "%USERPROFILE%\\Documents\\PowerShell\\Microsoft.PowerShell_profile.ps1" },
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

```json
"cleanup": { "enabled": true, "level": "safe",
             "include": ["developer.developer-mode", "developer.long-paths"] }
```

Every subsection is optional; a missing one adds no step. Validation errors
carry the JSON path and fail the profile (exit 2) before anything runs:

- `wsl.distro`: `Ubuntu`, `Ubuntu-24.04`, `Ubuntu-22.04` or `Debian` (the user
  step depends on the `sudo` group and `apt`). Default `Ubuntu`.
- `wsl.user`: required, `^[a-z_][a-z0-9_-]{0,31}$`, not `root`.
- `wsl.wslconfig`: optional; `memory` and `swap` match `^[0-9]+(MB|GB)$`,
  `processors` is an integer from 1 to the logical processor count.
- `git`: at least one key; `name` and `email` have no control characters;
  `email` contains `@`; `defaultBranch` matches `^[A-Za-z0-9._/-]+$`;
  `autocrlf` is `true`, `false` or `input`; `editor` has no control
  characters.
- `ssh.keyType`: `ed25519` (default) or `rsa` (4096 bits); `comment` matches
  `^[A-Za-z0-9._@+-]{1,100}$`, default `<user>@<computer name>`.
- `dotfiles`: exactly one of `repo` or `path`. `repo` is `https://…` or
  `git@host:path`; `path` is a folder, relative paths resolve against the
  profile's directory. `cloneTo` defaults to `%USERPROFILE%\dotfiles`.
  `files[].from` is relative and has no `..` segment; `files[].to`, after
  expanding environment variables, is an absolute path inside
  `%USERPROFILE%`. At least one file.
- `shell.wsl` requires `dev.wsl`. `theme` matches `^[A-Za-z0-9._-]+$`;
  `plugins` are Oh My Zsh built-in plugin names (same pattern) or the two
  external ones in the allowlist (`zsh-autosuggestions`,
  `zsh-syntax-highlighting`). `ohMyZsh` requires `zsh`.
- `shell.windows.theme` matches `^[A-Za-z0-9._-]+$`; `font` is a Nerd Font
  name accepted by `oh-my-posh font install` (`^[A-Za-z0-9-]+$`), default
  `Meslo`.
- `devDrive.path`: absolute, ends in `.vhdx`, on a local fixed drive.
  `sizeGB` ≥ 50 (Microsoft's minimum). `letter` is a single letter other than
  `C`. `label` matches `^[A-Za-z0-9 _-]{1,32}$`, default `Dev`. `caches` items
  are `npm`, `nuget`, `pip`, `cargo`.
- An enabled section with no subsections is valid and does nothing.

## 6. Execution

Every step is idempotent: a second run reports "already in order" for what is
done. Each item yields one outcome — done, already in order, pending (with
the command to run), not applicable, or failed (with a next action) — and a
line in the step details. Pending items make the step "completed with
failures" so the run exits 1 and the report lists them.

"Someone can type" means console input is not redirected and an interactive
user is signed in (`IInteractiveConsole`). The resume task after a reboot
opens a console, so a resumed run is interactive when the user is there.

### 6.1 `dev.wsl`

1. `wsl --status` fails or WSL is missing → `wsl --install --no-distribution`;
   when it asks for a reboot the step returns `RebootRequired` and the run
   resumes after the reboot.
2. Distro not in `wsl --list --quiet` → `wsl --install <distro> --no-launch`.
3. User missing (`id -u <user>` as root fails) → as root: `useradd -m -G sudo
   -s /bin/bash <user>`; then, if someone can type, `passwd <user>` with the
   console attached; then write `[user]\ndefault=<user>` into
   `/etc/wsl.conf` (keeping its other sections) and `wsl --terminate <distro>`.
   If nobody can type, the user is created with a locked password and the
   item ends pending with `formatkit dev wsl-user`.
4. `wslconfig` present → write `%USERPROFILE%\.wslconfig` (`[wsl2]` with the
   given keys) when the content differs; a differing existing file is backed
   up first (section 6.4 naming).
5. WSL distros are registered per user. When the elevated process's user is
   not the signed-in user, the step fails with "run FormatKit from the account
   that will use WSL" instead of installing for the wrong account.

### 6.2 `dev.git`

`GitLocator` finds `git.exe`; not found → failed, "add `development.git` to
programs". For each given key, `git config --global --get` and set it only if
different (`user.name`, `user.email`, `init.defaultBranch`, `core.editor`,
`core.autocrlf`).

### 6.3 `dev.ssh`

1. `%USERPROFILE%\.ssh\id_<type>` exists → already in order for the key.
2. Otherwise, if someone can type, run `ssh-keygen -t <type> [-b 4096] -C
   <comment> -f <file>` with the console attached (the user types the
   passphrase or leaves it empty); if nobody can type → pending with
   `formatkit dev ssh-key`.
3. Service `ssh-agent`: startup type Automatic, started.
4. If someone can type and the key is not in `ssh-add -l`, run `ssh-add
   <file>` with the console attached.
5. `git config --global core.sshCommand C:/Windows/System32/OpenSSH/ssh.exe`
   when Git is present, so Git uses the Windows agent.
6. Print the public key and `https://github.com/settings/ssh/new` on the
   console and in the report details.

### 6.4 `dev.dotfiles`

1. `repo`: `cloneTo` missing → `git clone <repo> <cloneTo>`; present with the
   same `origin` → `git pull --ff-only`; present with another origin or not a
   repository → failed, "choose another `cloneTo`". Private repositories rely
   on Git Credential Manager prompting when someone can type. `path`: used in
   place.
2. For each file: source missing → failed for that file. Target identical
   (byte compare) → already in order. Target different → rename it to
   `<target>.formatkit-<yyyyMMdd-HHmmss>.bak`, then copy. Target missing →
   create its folder and copy.

### 6.5 `dev.shell`

**WSL side** (needs the distro and user from 6.1; otherwise pending):

1. `zsh`: `command -v zsh` fails → as root `apt-get update` and `apt-get
   install -y zsh`.
2. `ohMyZsh`: `~/.oh-my-zsh` missing → as the user, `git clone --depth 1
   https://github.com/ohmyzsh/ohmyzsh.git ~/.oh-my-zsh` (after `apt-get
   install -y git` if needed). External plugins from the allowlist are cloned
   into `~/.oh-my-zsh/custom/plugins/<name>` from their GitHub repositories
   (`zsh-users/zsh-autosuggestions`, `zsh-users/zsh-syntax-highlighting`).
3. `~/.zshrc`: missing or written by FormatKit (first line marker `# Managed
   by FormatKit`) → write it from Oh My Zsh's template with `ZSH_THEME` and
   `plugins` set. A `.zshrc` without the marker is never replaced (warning).
4. Login shell: as root `chsh -s $(command -v zsh) <user>` when it differs.

**Windows side:**

1. `oh-my-posh` not found → failed, "add `development.oh-my-posh` to
   programs".
2. Font: `oh-my-posh font install <font>` as the signed-in user (user
   context); already installed fonts report already in order.
3. PowerShell profile (`$PROFILE` for PowerShell 7 if installed, and Windows
   PowerShell 5.1): when it does not already contain `oh-my-posh init`, append
   a block between `# >>> FormatKit oh-my-posh >>>` and `# <<< FormatKit
   oh-my-posh <<<` that runs `oh-my-posh init pwsh --config <theme>`. Runs
   after `dev.dotfiles`, so a profile brought by dotfiles is respected.
4. Windows Terminal font: write the fragment
   `%LOCALAPPDATA%\Microsoft\Windows Terminal\Fragments\FormatKit\formatkit.json`
   that updates the built-in PowerShell profiles' `font.face` to the Nerd
   Font's family name. Whether a fragment can change an existing profile's
   font is verified in the VM during implementation; if it cannot, the step
   prints the font name to set by hand and the report says so.

### 6.6 `dev.devdrive`

1. Windows build below 22621.2338 → not applicable, "update Windows".
2. `path` already attached with a volume labelled `label` at `letter` →
   already in order for the drive. `path` exists but is not attached →
   attach it and check the label; a different label → failed, "choose another
   path".
3. Letter in use by another volume, or free space on the host drive below
   `sizeGB` + 10 GB → failed with the reason, before creating anything.
4. Create: `diskpart` script `create vdisk file=<path> maximum=<MB>
   type=expandable`, `attach vdisk`, `convert gpt`, `create partition
   primary`, `assign letter=<letter>`; then `Format-Volume -DriveLetter
   <letter> -DevDrive -NewFileSystemLabel <label>`. A failure after the VHDX
   exists detaches it and deletes the file, so a retry starts clean.
5. Remounting after a reboot: whether Windows re-attaches a VHDX created this
   way is verified in the VM during implementation. If not, the step registers
   a scheduled task `FormatKit-DevDrive` (at startup, SYSTEM) that runs
   `Mount-DiskImage -ImagePath <path>`.
6. Caches: for each, create `<letter>:\packages\<name>` and set the user
   variable — `npm_config_cache`, `NUGET_PACKAGES`, `PIP_CACHE_DIR`,
   `CARGO_HOME` — unless it already points elsewhere (warning, left alone).
   Existing cache contents are not moved.

## 7. Cleanup category `developer`

Items are registry actions with the usual what / breaks / gain text in English
and pt-BR, `level` `safe`, and undo through the cleanup journal.
`ItemSelector` skips the `developer` category when selecting by level; only
`include` selects them. The category runs as step `cleanup.developer` after
the existing three.

| id | Change |
|---|---|
| `developer.developer-mode` | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock` `AllowDevelopmentWithoutDevLicense` = 1 |
| `developer.long-paths` | `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem` `LongPathsEnabled` = 1 |
| `developer.show-extensions` | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` `HideFileExt` = 0 |
| `developer.show-hidden` | same key, `Hidden` = 1 |
| `developer.sudo` | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Sudo` `Enabled` = 3 (inline); not applicable when `%SystemRoot%\System32\sudo.exe` is missing (before 24H2) |
| `developer.ps-execution-policy` | `HKLM\SOFTWARE\Microsoft\PowerShell\1\ShellIds\Microsoft.PowerShell` `ExecutionPolicy` = `RemoteSigned` (Windows PowerShell 5.1) |

Each value is confirmed in the VM during implementation; an item whose effect
cannot be confirmed is dropped and named in the commit message.

## 8. Commands

- `dev wsl-user [--profile <file>]`: runs 6.1 step 3 for the profile's distro
  and user, interactively. Exit 0 done, 1 failed, 2 invalid profile or
  console redirected.
- `dev ssh-key [--profile <file>]`: runs 6.3 steps 1–6 interactively. Same
  exit codes.

Without `--profile`, both use the defaults (`Ubuntu`, current Windows user
name lowercased if it matches the pattern; `ed25519`). Neither needs
elevation except when `ssh-agent` must be set to Automatic, which relaunches
elevated as other commands do.

## 9. Report and logs

Each item is a line in the step details: done, already in order, pending
(with its command), not applicable, or failed (with the next action). The SSH
public key and the GitHub link appear in the details. Logs have the full
output of `wsl`, `git`, `diskpart` and `oh-my-posh`, never anything typed into
`passwd`, `ssh-keygen` or `ssh-add` (those run with the console attached and
are not captured).

## 10. Testing

- **Unit (fakes for process runner, file system, console, WSL, virtual
  disks, environment):**
  - `DevOptions`: every rule in section 5, including `..` in `from`, `to`
    outside the profile, `C` as letter, size below 50, unknown plugin, unknown
    distro, `shell.wsl` without `wsl`.
  - Each step: already in order on a second run; pending when nobody can
    type; one failed item does not stop the others; reboot path for WSL;
    wrong-account WSL; Git missing; dotfiles backup naming and identical-file
    skip; `.zshrc` without marker kept; profile block not duplicated; Dev
    Drive cleanup after a failed format; cache variable already set
    elsewhere.
  - Cleanup: `developer` items never selected by any level; selected by
    `include`; catalog schema and both languages.
- **CLI:** `dev wsl-user` / `dev ssh-key` exit codes and redirected console.
- **VM scenarios** (`docs/testing.md`), on a restored clean checkpoint with
  nested virtualization on:
  - 13: WSL from nothing, reboot and resume, typing the Linux password;
    second run already in order.
  - 14: Git and SSH; key typed with a passphrase; second run keeps the key.
  - 15: dotfiles from a public repository; `.bak` created for a differing
    file; shell setup with zsh, Oh My Zsh and Oh My Posh.
  - 16: Dev Drive created, survives a reboot, cache variables set; developer
    cleanup items applied and undone.

## 11. Review focus

1. Nested virtualization or virtualization disabled in firmware: `wsl
   --install` succeeds but WSL 2 cannot start; the item must fail with
   "enable virtualization in the BIOS/UEFI", not hang.
2. `wsl.exe` output is UTF-16 and localized; only exit codes and the
   `--quiet` distro list may be parsed.
3. A resumed run after the WSL reboot where the user is not at the machine:
   the password items must end pending, not wait forever on the console.
4. Dotfiles repositories with CRLF/LF differences making identical files
   look different (compare bytes as checked out; document it).
5. Disks with BitLocker or low free space during Dev Drive creation, and a
   letter taken by a USB drive between validation and creation.
