# Cleanup: tweaks FormatKit does not include

FormatKit's cleanup catalog leaves out some popular "optimization" tweaks on purpose.
Each one either breaks something, removes protection, or has no gain that Microsoft
documents. This page explains why, and how `formatkit cleanup check` reports each
one if a script or guide already applied it.

`cleanup check` reads the machine and changes nothing. Each finding comes with a
fix, usually a command. Run fix commands in an elevated PowerShell (Run as
administrator).

Some tweaks below are **not detected** by `cleanup check`. Each section says so
plainly and explains why: usually Microsoft does not document the value, so
FormatKit cannot say where Windows reads it or what the default is.

## Disabling HPET or using `bcdedit /set useplatformclock`

**What people claim:** turning off the High Precision Event Timer (HPET) device, or
forcing or removing the platform clock with `bcdedit`, lowers input lag and raises
frame rates.

**What actually happens:** Windows chooses its timer source on its own. Disabling the
HPET device or overriding the platform clock has no gain documented by Microsoft, and
it can make timers and some drivers behave badly.

**How `cleanup check` reports it:** a disabled HPET device (hardware id `ACPI\PNP0103`)
is listed under **Breaks Windows features**. `cleanup check` does **not** detect the
`useplatformclock` boot setting, because reading the boot configuration needs administrator rights and parsing
`bcdedit` output. Check it yourself in an elevated PowerShell:

```
bcdedit /enum {current}
```

If the output has a `useplatformclock` line, remove it with
`bcdedit /deletevalue useplatformclock` and restart.

## Disabling devices

**What people claim:** disabling "unused" system devices in Device Manager frees
resources.

**What actually happens:** these devices use almost no resources, and some are needed
by features you may use later:

- Microsoft Virtual Drive Enumerator (`ROOT\VDRVROOT`): mounting ISO and VHD files.
- UMBus Root Bus Enumerator (`ROOT\UMBUS`): devices with user-mode drivers, such as
  some phones, printers and media players.
- NDIS Virtual Network Adapter Enumerator (`ROOT\NDISVIRTUALBUS`): virtual network
  adapters used by VPNs, Hyper-V and WSL.
- High precision event timer (`ACPI\PNP0103`): see the HPET section above.

**How `cleanup check` reports it:** each of these devices, when disabled, is listed
under **Breaks Windows features**, matched by hardware id so it works in any display
language.

## Disabling services

**What people claim:** disabling Windows services makes the PC faster.

**What actually happens:** most services start only when needed and use nothing while
stopped. Disabling them breaks the features that need them, often much later and with
no clear error: Windows Update, the Microsoft Store, time sync, the firewall,
networking, Xbox sign-in, WSL and Hyper-V. FormatKit's catalog never disables any of
the services on its protected list.

**How `cleanup check` reports it:** every protected service that is disabled is listed
under **Breaks Windows features**, with a fix that turns it back on:

- Services that Windows 11 starts at boot are set to Automatic (Update Orchestrator
  to Automatic, delayed start).
- The others are set to Manual, so Windows starts them when needed.
- Most fixes use `sc.exe config`. Windows refuses `sc.exe` for some services, even
  for administrators (DNS Client, Defender, firewall, Base Filtering Engine, Task
  Scheduler, Client License, AppX Deployment), so their fix uses `reg add` on the
  service's `Start` value instead.
- No administrator command can turn the Windows Security service
  (`SecurityHealthService`) back on, because Windows protects its settings. The fix
  points to an in-place repair: Settings > System > Recovery > Fix problems using
  Windows Update.

`cleanup check` does **not** detect a disabled OpenSSH Authentication Agent
(`ssh-agent`), because Windows ships it disabled.

## `DisablePagingExecutive`

**What people claim:** keeping the kernel and drivers in RAM
(`HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management`
`DisablePagingExecutive` = 1) makes Windows faster.

**What actually happens:** Microsoft documents this value for temporary diagnosis,
such as kernel stack tracing, and warns that it raises memory use. No speed gain is
documented.

**How `cleanup check` reports it:** listed under **Contested tweaks** when the value is 1.

## Nagle and delayed ACK (`TcpAckFrequency`, `TCPNoDelay`)

**What people claim:** setting `TcpAckFrequency` = 1 (and `TCPNoDelay` = 1) under
`HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\<adapter>` lowers
ping in online games.

**What actually happens:** `TcpAckFrequency` = 1 makes Windows acknowledge every TCP
packet at once. Microsoft documents it for specific server workloads and advises
against changing it without careful study. Most online games send their real-time data
over UDP, which this setting does not touch. Microsoft does not document a
`TCPNoDelay` value for the Windows TCP/IP stack; programs turn off Nagle for their own
connections in code.

**How `cleanup check` reports it:** `TcpAckFrequency` = 1 on any adapter is listed
under **Contested tweaks**. `cleanup check` does **not** detect `TCPNoDelay`, because Microsoft
does not document it for Windows.

## `MouseDataQueueSize` and `KeyboardDataQueueSize`

**What people claim:** shrinking the mouse and keyboard input buffers lowers input
lag.

**What actually happens:** these values only set how many input events the driver can
hold before it processes them. A smaller buffer does not make processing faster; if it
is too small, input can be dropped. The default is 100.

**How `cleanup check` reports it:** a `MouseDataQueueSize` other than 100 under
`HKLM\SYSTEM\CurrentControlSet\Services\mouclass\Parameters` is listed under
**Contested tweaks**. `cleanup check` does **not** detect `KeyboardDataQueueSize`: Microsoft
documents it only
under the PS/2 driver key (`i8042prt\Parameters`), not under the `kbdclass` key that
guides change, so FormatKit cannot say where Windows reads it.

## `GlobalTimerResolutionRequests`

**What people claim:** setting `GlobalTimerResolutionRequests` = 1 under
`HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel` brings back the old
system-wide timer resolution and lowers input lag.

**What actually happens:** since Windows 10 version 2004, a program's timer resolution
request applies to that program only, as Microsoft documents for `timeBeginPeriod`.
Microsoft also notes that a higher resolution can reduce overall performance and stop
the processor from saving power. The registry value itself is not documented by
Microsoft.

**How `cleanup check` reports it:** it does **not** detect it, because Microsoft does
not document the value.

## Registry cleaners

**What people claim:** removing "invalid" or "leftover" registry entries makes Windows
faster and more stable.

**What actually happens:** unused registry entries cost nothing measurable, and
cleaners can delete entries that apps or Windows still need. Microsoft does not
support registry cleaners.

**How `cleanup check` reports it:** it does **not** detect registry cleaners, because a
cleaner leaves no single value to look for.

## Turning off Virtual Machine Platform

**What people claim:** turning off the Virtual Machine Platform optional feature
improves gaming performance.

**What actually happens:** Microsoft lists it as an option that may help some games,
but WSL 2, Windows Sandbox, Hyper-V and Android apps need it. FormatKit's later
features also need it, so the cleanup catalog does not turn it off.

**How `cleanup check` reports it:** it does **not** detect it, because it is an optional
feature, not a registry value, and `cleanup check` has no rule for optional features. Check it in **Turn Windows features on or off**.

## Disabling Windows Update or Microsoft Defender

**What people claim:** turning off automatic updates or the antivirus saves resources
and stops interruptions.

**What actually happens:** the PC stops getting security fixes, or loses its
antivirus. Scripts often do this with policies that are hard to find later.

**How `cleanup check` reports it:** listed under **Breaks Windows features**:

- `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU` `NoAutoUpdate` = 1
  (automatic updates off by policy).
- `HKLM\SOFTWARE\Policies\Microsoft\Windows Defender` `DisableAntiSpyware` = 1
  (Defender off by policy; current Windows ignores it while Tamper Protection is on).
- The Windows Update, Update Orchestrator, Defender, Security and firewall services
  when disabled (see "Disabling services").

## Disabling more services: SysMain, search, notifications, sharing and others

**What people claim:** disabling SysMain, Windows Search (`WSearch`), push
notifications (`WpnService`), Connected Devices Platform (`CDPSvc`), Server
(`LanmanServer`), IP Helper (`iphlpsvc`), Delivery Optimization (`DoSvc`),
Windows Error Reporting (`WerSvc`) or the Bluetooth services frees memory and
speeds the PC up.

**What actually happens:** FormatKit's test VM measured no idle memory gain from
changing service start types; disabling SysMain even showed slightly more memory in
use. Each of these services backs a feature:

- `SysMain`: preloads apps you use often; without it, apps open a little slower
  from cold.
- `WSearch`: Start and File Explorer search.
- `WpnService`: notifications from the Store, Xbox, Teams and other apps.
- `CDPSvc`: Phone Link, Nearby sharing and the clipboard across devices.
- `LanmanServer`: shared folders and printers on this PC.
- `iphlpsvc`: IPv6 transition (Teredo), which Xbox online play can use.
- `DoSvc`: downloads for Windows Update and the Microsoft Store (reported broken in
  [privacy.sexy #462](https://github.com/undergroundwires/privacy.sexy/issues/462)).
  To stop sharing downloads with other PCs, use the
  `privacy.delivery-optimization-no-peers` item instead.
- `WerSvc`: crash reports that Microsoft and app vendors use to fix bugs.
- Bluetooth (`bthserv`, `BTAGService`, `BthAvctpSvc`): these already ship Manual and
  start only when a device needs them; disabling them breaks Bluetooth and Bluetooth
  audio ([winutil #2685](https://github.com/ChrisTitusTech/winutil/issues/2685),
  [#2877](https://github.com/ChrisTitusTech/winutil/issues/2877)).

The Xbox and Windows Update services are on FormatKit's protected list (see
"Disabling services").

**How `cleanup check` reports it:** each of the services above, when disabled, is
listed under **Breaks Windows features** with a fix that sets it back to its Windows
default start type, as are the Xbox and Windows Update services, including Xbox Live
Networking (`XboxNetApiSvc`) and Windows Update Medic (`WaaSMedicSvc`). A disabled
`SysMain` is listed under **For your information** instead, because the only cost is a slower
cold start of apps. Windows refuses `sc.exe` for `WaaSMedicSvc`, so its fix uses
`reg add` on the service's `Start` value.

## Turning off Game Bar integration

**What people claim:** turning off the Game Bar and Xbox capture integration makes
games run smoother.

**What actually happens:** no gain is measured, and turning the integration off has
been reported to cause stutter on CPUs with performance and efficiency cores
([Win11Debloat #784](https://github.com/Raphire/Win11Debloat/issues/784)) and an
`ms-gamebar` error popup when a game opens the Game Bar. To stop background
recording, turn it off in Settings > Gaming > Captures.

**How `cleanup check` reports it:** the documented policy that turns game recording
and broadcasting off
([`AllowGameDVR`](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-applicationmanagement)
= 0 under `HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR`) is listed under
**Contested tweaks**. `cleanup check` does **not** detect the per-user Game Bar
values, because Microsoft does not document them.

## Turning off background apps globally

**What people claim:** blocking every app from running in the background saves
resources.

**What actually happens:** no gain is measured, and apps that need to run in the
background stop working; the Snipping Tool overlay is one reported case
([winutil #3401](https://github.com/ChrisTitusTech/winutil/issues/3401)). Turn it off
per app in Settings > Apps > Installed apps > Advanced options instead. Microsoft
warns that blocking background activity can stop message, email and voicemail
notifications.

**How `cleanup check` reports it:** the documented policy that denies every app
([`LetAppsRunInBackground`](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy)
= 2 under `HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy`) is listed under
**Breaks Windows features**. `cleanup check` does **not** detect the per-user switch,
because Microsoft does not document its value.

## Turning off multiplane overlay (MPO)

**What people claim:** turning off multiplane overlay (`OverlayTestMode`) fixes
flicker and stutter.

**What actually happens:** no gain is measured, and it has been reported to make
`dwm.exe` crash, in a way a restore point does not undo
([winutil #4975](https://github.com/ChrisTitusTech/winutil/issues/4975)). Flicker is
better fixed with a GPU driver update.

**How `cleanup check` reports it:** it does **not** detect it, because it has no rule
for it.

## Turning off IPv6

**What people claim:** turning off IPv6 (`DisabledComponents` = 255) and Teredo makes
the network faster.

**What actually happens:** no gain is measured. Windows prefers IPv6, and Microsoft
does not recommend turning it off because some Windows components might not work
([Microsoft Learn](https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/configure-ipv6-in-windows)).
To prefer IPv4, Microsoft documents `DisabledComponents` = 32 instead.

**How `cleanup check` reports it:** `DisabledComponents` = 255 under
`HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters` is listed under **Breaks
Windows features**. Other values, such as 32, are not reported. `cleanup check` does
**not** detect Teredo turned off on its own, because it has no rule for it.

## Blocking telemetry in the hosts file

**What people claim:** adding Microsoft telemetry hosts to the `hosts` file stops
Windows from sending data.

**What actually happens:** the lists also block hosts that Windows Update and other
services need ([privacy.sexy #462](https://github.com/undergroundwires/privacy.sexy/issues/462)).
Microsoft says `settings-win.data.microsoft.com` must not be blocked
([Microsoft Learn](https://learn.microsoft.com/en-us/windows/privacy/configure-windows-diagnostic-data-in-your-organization)).
FormatKit's privacy items lower diagnostic data with the documented policies
instead.

**How `cleanup check` reports it:** it does **not** detect it, because it has no rule
for the `hosts` file.

## Denying access to the Start search `store.db`

**What people claim:** a deny ACL on the Start search `store.db` file blocks Store
suggestions in search.

**What actually happens:** Start search can come back empty, and neither a registry
backup nor a restore point undoes it
([Win11Debloat #830](https://github.com/Raphire/Win11Debloat/issues/830)). The
`privacy.start-web-search` item turns web results off with a documented policy.

**How `cleanup check` reports it:** it does **not** detect it, because it has no rule
for file permissions.

## `DISM /StartComponentCleanup /ResetBase`

**What people claim:** it shrinks the WinSxS folder and frees disk space.

**What actually happens:** it does free space, but installed updates can no longer be
uninstalled, and nothing undoes it. Windows already cleans up the component store on
its own schedule.

**How `cleanup check` reports it:** it does **not** detect it, because it leaves no
setting to read.

## Turning off BitLocker or reserved storage

**What people claim:** turning off BitLocker device encryption, or reserved storage,
frees space and speeds up the disk.

**What actually happens:** turning off BitLocker leaves the data on the disk readable
by anyone who takes it. Reserved storage keeps space for updates; without it, feature
updates can fail for lack of space, so it has to be turned back on before one.

**How `cleanup check` reports it:** it does **not** detect either, because it has no
rule for them.

## Tweaks left out because Microsoft does not document them

These were planned for the catalog but dropped, because Microsoft documentation
could not confirm where Windows stores them or what they do, or because the value
could not be checked. FormatKit does not apply them, and `cleanup check` does **not**
detect them, for the same reason.

- **Suggestions in Settings and the "welcome experience" after updates**
  (`ContentDeliveryManager` values `SubscribedContent-338393Enabled`,
  `SubscribedContent-353694Enabled`, `SubscribedContent-353696Enabled` and
  `SubscribedContent-310093Enabled`). These values are not present in the test VM,
  so FormatKit cannot confirm what Windows reads. Turn them off in Settings.
- **Suggestions in Start and tips on the lock screen** (`SystemPaneSuggestionsEnabled`,
  `SubscribedContent-338388Enabled`, `RotatingLockScreenOverlayEnabled` and
  `SubscribedContent-338387Enabled`). Not verified: the Settings toggle may use values
  that are absent until it is first used, so writing only the values present in the
  test VM may change nothing. Turn them off in Settings > Personalization > Start and
  > Lock screen. The Start recommendations are turned off by
  `interface.start-recommendations-off`.
- **Cross Device Experience Host** (`MicrosoftWindows.CrossDevice`), which the design
  planned to remove with Phone Link. No Microsoft Store id was found for it, so its
  removal could not be undone; `apps.phone-link` removes only Phone Link.
- **Tips app** (`Microsoft.Getstarted`) and **Dev Home**: they cannot be reinstalled
  from the Microsoft Store, so removing them could not be undone.
- **Hardware-accelerated GPU scheduling** (`HwSchMode`). Microsoft documents only the
  switch in Settings > System > Display > Graphics.
- **Network throttling** (`NetworkThrottlingIndex`). The only Microsoft article was
  retired.
- **Game Bar background recording** (`AppCaptureEnabled`, `GameDVR_Enabled`). Turn it
  off in Settings > Gaming > Captures.
- **Taskbar search box mode** (`SearchboxTaskbarMode`), **End task in the taskbar**
  (`TaskbarEndTask`), **File Explorer opening to This PC** (`LaunchTo`), the
  **classic context menu** (the `{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}` CLSID key)
  and **taskbar animations** (`TaskbarAnimations`). Microsoft does not document these
  values. Change them in Settings or in File Explorer's options.
- **No volume ducking during calls** (`UserDuckingPreference`). Microsoft does not
  document the value. Set it in Control Panel > Sound > Communications.
- **Copilot in Edge** (`HubsSidebarEnabled`). Since Edge 141 that policy no longer
  controls the Copilot button, and the policy that does applies only to work or school
  profiles.
