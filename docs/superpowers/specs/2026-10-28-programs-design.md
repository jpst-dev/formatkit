# FormatKit — Programs Design

- **Date:** 2026-10-28
- **Status:** Draft, awaiting review
- **Scope:** Sub-project 4a (Programs): installing the user's programs after a
  clean install, from a curated catalog, any winget id, a list exported from the
  old PC, and direct installers outside winget
- **Builds on:** `2026-10-27-core-and-drivers-design.md` (profile, step engine,
  resume, logs, report, exit codes) and `2026-10-27-cleanup-design.md`
  (embedded catalog pattern, localization, item outcomes)

## 1. Purpose

After a clean install, put back the programs the user actually uses, in one
unattended run, with a clear per-program result. Nothing is chosen for the
user: the catalog only makes good choices easy to find.

## 2. Scope

**In 4a:**

- A `programs` module run after drivers and cleanup.
- Sources: catalog ids, free winget ids (sources `winget` and `msstore`), the
  JSON produced by `winget export` on the old PC, and direct installers (https
  URL with mandatory SHA-256, or a local file).
- Optional upgrade of programs that are already installed, and optional version
  pin per winget item.
- Per-user installs for installers that refuse or misbehave when elevated.
- Commands `programs list`, `programs search`, `programs export`.

**Later, sub-project 4b (dev environment):** WSL, Git and SSH setup, dotfiles,
Dev Drive. 4b reuses this module to install tools; 4a only installs them.

**Out of scope:** uninstalling programs; undo; creating shortcuts; configuring
programs (default browser, sign-in, settings); an interactive picker in the CLI
(the WPF interface, sub-project 7, does that); migrating program data.

## 3. Decisions

| # | Decision | Reason |
|---|---|---|
| D1 | Own module calling winget once per program; results come from exit codes only (approach 1). | Per-program result and resume; never parses localized text. `winget import` is all-or-nothing; the COM API adds registration and elevation problems. |
| D2 | The profile is the program list. The CLI only helps build it (`list`, `search`, `export`). | No UI that the WPF app would redo. |
| D3 | Any item selected requires `"acceptAgreements": true` in the profile; without it the profile is invalid. | Installing accepts each package's license; FormatKit never accepts terms the user did not confirm. |
| D4 | Direct installers from a URL require https and a SHA-256; a local file may carry one. A mismatch deletes the file and runs nothing. | The same trust bar winget applies. |
| D5 | An installed program is reported as "already installed"; `"upgrade": true` upgrades instead. Winget items may pin `version`. | Re-runs and resumes are safe by default. |
| D6 | Catalog items or winget items marked `"context": "user"` install as the signed-in user, not elevated. | Some installers (Spotify, Discord, Zoom) fail or install into the wrong profile when elevated. |
| D7 | One failed program never stops the others. | A reinstall with 30 programs must not stop at the 3rd. |
| D8 | The module runs after `drivers` and `cleanup`. | Network and a clean system first; cleanup never removes what the user just installed. |
| D9 | No undo and no uninstall. | Out of scope; the user can uninstall from Settings. |

## 4. Architecture

New project `FormatKit.Programs`, referencing only `FormatKit.Core`.

| Unit | Responsibility |
|---|---|
| `Catalog/ProgramCatalog` + embedded `programs.json` | Curated entries: id, category, winget id, source, context, en/pt-BR name and description. Validated by `ProgramCatalogValidator`. |
| `ProgramsOptions` | Parses and validates the profile section (section 5). |
| `ProgramSelector` | Builds the ordered item list from catalog ids, free winget ids, the import file and direct entries; removes duplicates (same winget id and source). |
| `Steps/WingetCategoryStep` | One step per catalog category, plus `programs.other` for free and imported winget ids. Items run in order; each yields an outcome. |
| `Steps/DirectInstallStep` | `programs.direct`: download, verify, run each direct installer. |
| `Core/SystemAccess/WingetClient` (extended) | Adds `UpgradeAsync`, `SearchAsync`, `ExportAsync`, `--version`, exit-code mapping (section 6). |
| `Core/SystemAccess/DirectInstaller` + `IHttpDownloader` | https download with timeout, SHA-256 check, `.exe` with given args or `.msi` via `msiexec /i <file> /qn /norestart`, exit-code mapping. |
| `Core/SystemAccess/UserContextRunner` | Runs one command as the signed-in user, non-elevated, through a temporary scheduled task (`/IT /RL LIMITED`), waits for it and returns its exit code. |
| `ProgramsModule` | `IModule` named `programs`: validation and step creation. |
| CLI `Commands/ProgramsCommands` | `programs list/search/export`. |

Every external value placed on a command line (winget ids, versions, sources)
must match `^[A-Za-z0-9._+-]+$`; direct installer arguments are passed as an
argument list, never through a shell.

## 5. Profile

```json
"programs": {
  "enabled": true,
  "acceptAgreements": true,
  "upgrade": false,
  "catalog": ["browsers.firefox", "communication.discord", "games.steam"],
  "winget": [
    { "id": "Notepad++.Notepad++" },
    { "id": "Python.Python.3.12", "version": "3.12.7" },
    { "id": "9NBLGGH4NNS1", "source": "msstore" },
    { "id": "Some.UserApp", "context": "user" }
  ],
  "importFile": "C:\\backup\\old-pc-programs.json",
  "direct": [
    { "name": "My app", "url": "https://example.com/setup.exe", "sha256": "<64 hex>", "args": ["/S"] },
    { "name": "Local tool", "path": "D:\\setups\\tool.msi" }
  ]
}
```

Validation (errors carry the JSON path, like the other modules):

- `acceptAgreements` must be `true` when any item is selected.
- `catalog` ids must exist in the catalog.
- `winget[].id` and `version` must match the safe pattern; `source` is `winget`
  (default) or `msstore`; `context` is `machine` (default) or `user`.
- `importFile` must exist and be a `winget export` JSON (`Sources[].Packages[].PackageIdentifier`,
  source from `Sources[].SourceDetails.Name`). Read when steps are created;
  unreadable or malformed → profile error.
- `direct[]`: `name` required; exactly one of `url` or `path`; `url` must be
  https and needs `sha256` (64 hex characters); `path` may have `sha256`;
  `.msi` ignores `args` (uses the fixed `msiexec` line), `.exe` uses `args`
  (default none); other extensions are errors.
- An enabled section with no items at all is valid and does nothing.

## 6. Execution

**Winget item:**

1. `winget list --id <id> --exact` (with `--source`). Installed and
   `upgrade` false → "already installed".
2. Installed and `upgrade` true → `winget upgrade --id <id> --exact --silent`;
   "no applicable update" counts as success ("up to date").
3. Not installed → `winget install --id <id> --exact --silent
   --disable-interactivity --accept-package-agreements
   --accept-source-agreements` (+ `--source`, `--version`).
4. The exit code maps to an outcome. Known codes (from Microsoft's winget
   return-code documentation, confirmed during implementation): success,
   already installed, reboot required to finish, no applicable update, package
   not found, no network / source unavailable, blocked by policy, installer
   failed. Each failure has a localized message with a next action; an unknown
   code is "failed (code X), see the log". Full output goes to the log only.

**User context:** items with `context: user` run the same winget command
through `UserContextRunner` as the signed-in user. If nobody is signed in, the
item fails with the next action "sign in and run again". The engine never
re-runs a finished step, and FormatKit always runs inside a signed-in session
(CLI or the resume task at logon). This replaces an earlier "defer to logon"
idea; see plan ruling P1.

**Winget missing:** before the first winget item, the step checks `winget
--version`. If missing, it tries the documented registration
`Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe`
and checks again. Still missing → every winget item fails with "install App
Installer from the Microsoft Store"; direct items still run.

**Direct item:**

1. URL → download to `%ProgramData%\FormatKit\downloads\<name-hash>` with a
   timeout; path → use the file in place.
2. Verify SHA-256 when given (always for URLs). Mismatch → delete the download,
   fail the item, run nothing.
3. Run elevated; exit 0 → installed; 3010 / 1641 → installed, reboot required;
   anything else → failed with the code.
4. Delete the download after success; keep it after a failure for diagnosis.

**Reboots:** a "reboot required" item does not stop the step; the step reports
`RebootRequired` at the end and the engine reboots as for drivers.

**Resume and cancellation:** a step interrupted by a crash runs again on
resume. Winget items already installed report "already installed". Direct
items are recorded after success in `%ProgramData%\FormatKit\programs-direct.json`
(key `name|sha256`, or `name|path`) and skipped when recorded (plan ruling P2). Ctrl+C stops after the
current item; a running installer is never killed.

**Exit code:** any failed item → step "completed with failures" → run exit 1,
as today.

## 7. Catalog

Entry criteria: official publisher in winget or msstore, reliable silent
install, popular for people reinstalling Windows (general, gamer and developer
users). Excluded: optimizers and "cleaners", driver updaters, third-party
antivirus (Defender is enough; the README says so), installers bundling
adware, anything needing a crack. Edge and Windows Terminal ship with Windows
11 and are not listed.

Initial entries (each winget id confirmed with `winget show` during
implementation; an entry that cannot be confirmed is dropped and listed in the
commit message):

| Category | Entries |
|---|---|
| browsers | Chrome, Firefox, Brave, Opera GX, Vivaldi |
| communication | Discord (user), WhatsApp (msstore), Telegram, Zoom (user), Slack |
| games | Steam, Epic Games Launcher, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Playnite |
| media | VLC, Spotify (user), OBS Studio, Audacity, HandBrake, foobar2000 |
| utilities | 7-Zip, PowerToys, Everything, ShareX, Bitwarden, qBittorrent, CPU-Z, HWiNFO, CrystalDiskInfo |
| productivity | LibreOffice, Adobe Acrobat Reader, Notion, Obsidian, Thunderbird |
| development | VS Code, Git, Python 3, Node.js LTS, Docker Desktop, JetBrains Toolbox |

The catalog grows over time; adding an entry is a data change plus the catalog
test.

## 8. Commands

- `programs list [--category <c>]`: id, category, name, `[user]` marker;
  `--verbose` adds the description and winget id. Exit 0; bad category → 2.
- `programs search <term>`: runs `winget search` and prints id, name and source.
  This is the one place that shows winget's own text, passed through unparsed
  (it is for a human to read). Exit 0, or 1 when winget is missing.
- `programs export <file>`: runs `winget export -o <file>
  --include-versions false` on the old PC and prints how to use the file as
  `importFile`. Exit 0, or 1 on failure.

None of them needs elevation.

## 9. Report and logs

Each program is a line in the step details: installed, already installed, up
to date, upgraded, installed (reboot required), or failed
with the reason. The log has the full winget or installer output.

## 10. Testing

- **Unit:**
  - `WingetClient` with `FakeProcessRunner`: every mapped exit code, arguments
    with `--source` / `--version`, upgrade "no applicable update", winget
    missing.
  - `DirectInstaller` with a fake `IHttpDownloader`: right hash, wrong hash
    (file deleted, nothing run), `.msi` vs `.exe` command lines, 3010 / 1641,
    download error, local path with and without hash.
  - Profile validation: `acceptAgreements`, https and hash rules, unsafe ids,
    unknown catalog ids, `importFile` parsing and duplicates.
  - `UserContextRunner` with fake scheduled tasks and user session: success,
    failure code; nobody signed in → failed with "sign in and run again".
  - Steps: failures do not stop other items; resume skips completed items.
- **Catalog:** schema valid, unique ids, both languages present, dropped ids
  listed in the test.
- **CLI:** `list` filtering and exit codes, `search` / `export` argument
  building, no elevation.
- **VM scenarios** (`docs/testing.md`):
  - 10: a profile with a catalog item, a free winget id, an msstore item, a
    user-context item and a direct installer with its hash; run twice (second
    run: "already installed").
  - 11: a direct installer with a wrong hash fails without running.
  - 12: `programs export` on one VM, then `importFile` on a restored `clean`.

## 11. Review focus

The input classes most likely to bite users that unit tests can miss:

1. Fresh install where winget is not registered yet (first logon).
2. Installers that hang waiting for input despite `--silent`; the install
   timeout must end the item as failed, not the run.
3. User-context items when the run resumes before anyone signs in.
4. `importFile` from an older winget version (schema differences) and packages
   no longer in the source.
5. Large downloads on slow links (timeout and partial files).
