# Update from inside FormatKit

## Goal

The user should not have to download every release by hand. The app says when a newer stable release exists and installs it on request. The command line has the same update, on request only.

```
formatkit update [--check] [--yes]
```

Success means: from a build older than the latest release, `FormatKitUI.exe` shows the new version at start, one click replaces both executables and reopens the app, and a failure at any point leaves the old executables working.

The user chose this option over checking only on request and over winget: the app checks when it opens.

## Decisions

The user chose the check at start. The rest were delegated under the standing authorization of 2026-11-03. Each records the reason, so it can be challenged.

| # | Decision | Reason |
|---|---|---|
| D1 | The window checks once in the background when it opens. The command line never checks by itself. | A scripted or first-logon command must not depend on the network or print anything extra. |
| D2 | A setting turns the check off, on by default, in the existing `app.json`. | The check is a network call the user did not click on, so it can be declined. |
| D3 | Only the check is automatic. Downloading and replacing always need a click, or `--yes`. | The user keeps control over what runs on the PC. |
| D4 | No update while a run is in progress or interrupted, or while the window has work going on. | A reinstall must not change program in the middle, and the resume task starts the same path after a reboot. |
| D5 | Only stable releases, from `/repos/jpst-dev/formatkit/releases/latest`, and never a downgrade. | That endpoint already leaves out drafts and prereleases. An `-rc` build counts as older than its stable version. |
| D6 | Asset addresses must be `https://github.com/jpst-dev/formatkit/releases/download/...`. Anything else is refused. | A wrong API answer must not point the download elsewhere. |
| D7 | `SHA256SUMS.txt` is required, and the sizes and hashes must match before anything is replaced. | It detects a damaged download. It does not protect against a compromised GitHub account, because the sums live in the same place; only code signing would, which costs money and stays out of scope. |
| D8 | Both executables update together, when both are next to the running one. A missing one is not added. | They share one version, and the user may keep only one of them. |
| D9 | All downloads finish and verify first, then the swap renames each current file to `<name>.old` and moves the new one in. Any failure restores what was already swapped. | Windows allows renaming a running executable, so this works without a helper process, and a half-updated pair never remains. |
| D10 | `<name>.old` is removed at the next start of either executable, ignoring failures. An old file that cannot be replaced asks the user to close FormatKit. | A running program cannot delete itself. |
| D11 | The update runs without elevation. A folder that cannot be written is an error that names the folder. | Elevating to replace files is more power than a portable executable needs. |
| D12 | The window reopens by itself after an update. | One click should finish the job. It releases the single-instance mutex first, or the new process would only signal the old one and exit. |
| D13 | Code goes in `FormatKit.Hosting` (`Updater`), with the network behind a new `IReleaseSource` in Core. No new project. | Both front ends share it, like `ProfileCapture`. |

## Behavior

### Check

`IReleaseSource.GetLatestAsync` asks the GitHub API with a `FormatKit/<version>` User-Agent and a 15 second timeout. It sends nothing else: no identifier, no telemetry. It returns the tag, the release page and the assets.

`Updater.CheckAsync` gives one of: up to date, update available (current, latest, release page), or could not check (reason). The current version comes from the running assembly's informational version, without the `+commit` part. A version that cannot be parsed counts as could not check.

The window shows nothing when it could not check at start: a PC without network is normal before a reinstall. The check waits for nothing on the way to the first page and does not run on a `--resume` start or while a run state exists. A check from the About page does report the failure.

### Update

1. Refuse if the running file is not named `formatkit.exe` or `FormatKitUI.exe` (for example `dotnet run`), if a run state exists (D4), or if the release has no sums or lacks an asset that is needed.
2. For each executable present in the folder, download to `<name>.update` and compare size and SHA-256 with the release. A failure deletes every `.update` file and stops; the old files were not touched.
3. Delete any leftover `<name>.old`. If one cannot be deleted, stop and ask the user to close FormatKit.
4. Swap as in D9. Cancelling is honored until this step, and not during it.

### Command line

| Case | Output | Exit |
|---|---|---|
| `--check`, up to date or update available | The versions and the release page | 0 |
| Up to date | The version | 0 |
| Update available, console can prompt | Asks, `y` updates and anything else stops | 0 |
| Update available, no console, no `--yes` | Says how to update | 1 |
| Updated | Old and new version | 0 |
| Could not check, or the update failed | The reason; nothing changed | 1 |
| Refused (D4, wrong file name) | The reason | 2 |

### Window

- Home shows a banner like the interrupted run's: the new version, **Update now**, and a link to the release page. An interrupted run's banner takes the place of this one, since D4 would refuse anyway.
- About shows **Check for updates**, the result, and the setting of D2.
- During the update the buttons are disabled and the page says what it is doing. A run or media job cannot start meanwhile, and an update cannot start during one.
- A failure shows the reason and the app keeps working.

## Components

- `FormatKit.Core/SystemAccess/IReleaseSource.cs`, `GitHubReleaseSource.cs`: the API call and its JSON. Uses a shared `HttpClient`, like `HttpDownloader`.
- `FormatKit.Core/Updating/AppVersion.cs`: parse and compare `major.minor.patch[-prerelease]`.
- `FormatKit.Hosting/Updater.cs`: check, update, cleanup of `.old`. All file work goes through `IFileSystem` and `IHttpDownloader`.
- `FormatKit.Cli/Commands/UpdateCommand.cs`: the command.
- `FormatKit.App/ViewModels/UpdateViewModel.cs`: state shared by Home and About.
- `FormatKit.App/Services/AppSettings.cs`: gains `checkForUpdates`, keeping `lastProfile`.

## Error handling

Network errors, API errors, unexpected JSON, a wrong address, and a wrong size or hash become a localized failure with the reason. They never crash the window. The log records each step; it holds no secret, because there is none.

## Testing

- Unit: version ordering; every outcome of the check; each refusal; a failed download or hash leaves the originals and no `.update`; a failed swap restores the first file; only present executables are updated; `.old` cleanup; the exit codes; settings keep `lastProfile`; the window does not check when the setting is off or a run state exists.
- VM: build with `-p:Version=0.2.0`, which is older than the latest release, and run the real thing against GitHub: `update --check`, `update --yes` with both executables present, `--version` afterwards, and the window's banner and button. The published release has no update code yet, so the executables it installs prove the swap and not a second update.

## Out of scope

- Code signing (D7).
- Updating the copy in `C:\FormatKit`, unless the user runs the update from there.
- Background downloads, scheduled checks and release channels.
- Release notes inside the app: the banner links to the release page.

## Found while running

Filled in during the VM runs.
