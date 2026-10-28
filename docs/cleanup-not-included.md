# Cleanup: tweaks FormatKit does not include

FormatKit's cleanup catalog leaves out some popular "optimization" tweaks on purpose.
Each one either breaks something, removes protection, or has no gain that Microsoft
documents. This page explains why, and how `formatkit cleanup check` reports each
one if a script or guide already applied it.

`cleanup check` reads the machine and changes nothing. Each finding comes with a
command that puts the default back. Run the command in an elevated terminal.

## Disabling HPET or using `bcdedit /set useplatformclock`

**What people claim:** turning off the High Precision Event Timer (HPET) device, or
forcing or removing the platform clock with `bcdedit`, lowers input lag and raises
frame rates.

**What actually happens:** Windows chooses its timer source on its own. Disabling the
HPET device or overriding the platform clock has no gain documented by Microsoft, and
it can make timers and some drivers behave badly.

**How `cleanup check` reports it:** a disabled HPET device (hardware id `ACPI\PNP0103`)
is listed under **Breaks something**. The `useplatformclock` boot setting is **not**
checked, because reading the boot configuration needs administrator rights and parsing
`bcdedit` output. Check it yourself in an elevated terminal:

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
under **Breaks something**, matched by hardware id so it works in any display
language.

## Disabling services

**What people claim:** disabling Windows services makes the PC faster.

**What actually happens:** most services start only when needed and use nothing while
stopped. Disabling them breaks the features that need them, often much later and with
no clear error: Windows Update, the Microsoft Store, time sync, the firewall,
networking, Xbox sign-in, WSL and Hyper-V. FormatKit's catalog never disables any of
the services on its protected list.

**How `cleanup check` reports it:** every protected service that is disabled is listed
under **Breaks something**, with a command that sets it back to its Windows default
(`sc.exe config`, or `reg add` for the DNS Client and Defender services, which
`sc.exe` cannot change). The OpenSSH Authentication Agent (`ssh-agent`) is not checked,
because Windows ships it disabled.

## `DisablePagingExecutive`

**What people claim:** keeping the kernel and drivers in RAM
(`HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management`
`DisablePagingExecutive` = 1) makes Windows faster.

**What actually happens:** Microsoft documents this value for temporary diagnosis,
such as kernel stack tracing, and warns that it raises memory use. No speed gain is
documented.

**How `cleanup check` reports it:** listed under **Contested** when the value is 1.

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
under **Contested**. `TCPNoDelay` is not checked, since Windows does not document it.

## `MouseDataQueueSize` and `KeyboardDataQueueSize`

**What people claim:** shrinking the mouse and keyboard input buffers lowers input
lag.

**What actually happens:** these values only set how many input events the driver can
hold before it processes them. A smaller buffer does not make processing faster; if it
is too small, input can be dropped. The default is 100.

**How `cleanup check` reports it:** a `MouseDataQueueSize` other than 100 under
`HKLM\SYSTEM\CurrentControlSet\Services\mouclass\Parameters` is listed under
**Contested**. `KeyboardDataQueueSize` is not checked: Microsoft documents it only
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

**How `cleanup check` reports it:** not checked, because Microsoft does not document
the value.

## Registry cleaners

**What people claim:** removing "invalid" or "leftover" registry entries makes Windows
faster and more stable.

**What actually happens:** unused registry entries cost nothing measurable, and
cleaners can delete entries that apps or Windows still need. Microsoft does not
support registry cleaners.

**How `cleanup check` reports it:** a registry cleaner leaves no single value to
detect, so nothing is reported.

## Turning off Virtual Machine Platform

**What people claim:** turning off the Virtual Machine Platform optional feature
improves gaming performance.

**What actually happens:** Microsoft lists it as an option that may help some games,
but WSL 2, Windows Sandbox, Hyper-V and Android apps need it. FormatKit's later
features also need it, so the cleanup catalog does not turn it off.

**How `cleanup check` reports it:** not checked, because it is an optional feature,
not a registry value. Check it in **Turn Windows features on or off**.

## Disabling Windows Update or Microsoft Defender

**What people claim:** turning off automatic updates or the antivirus saves resources
and stops interruptions.

**What actually happens:** the PC stops getting security fixes, or loses its
antivirus. Scripts often do this with policies that are hard to find later.

**How `cleanup check` reports it:** listed under **Breaks something**:

- `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU` `NoAutoUpdate` = 1
  (automatic updates off by policy).
- `HKLM\SOFTWARE\Policies\Microsoft\Windows Defender` `DisableAntiSpyware` = 1
  (Defender off by policy; current Windows ignores it while Tamper Protection is on).
- The Windows Update, Update Orchestrator, Defender, Security and firewall services
  when disabled (see "Disabling services").
