# Testing LiteSynapse

## Build 15 (1.2.4): a Performance tab that says ON/OFF and HELPING/HURTING, and reads back what is set

**What to check first:** open Performance > WINDOWS on your already-tuned PC and press **CHECK AS ADMIN** once.
Most rows should read **ON · HELPING** (green). Anything that reads **HURTING** (orange) is something your
optimizer did not set, or set differently - read the row before switching it.

### The Performance tab, simplified

- Every row names the thing itself ("Pointer acceleration", "Print Spooler", "Steam") and has **one badge**:
  whether it is **ON** or **OFF**, and whether that is **HELPING** or **HURTING** performance. A security
  feature left on reads **SAFE DEFAULT**; something Windows will not tell an ordinary account reads
  **CAN'T TELL** until CHECK AS ADMIN reads it.
- The **switch** on the right shows the side it is on. Flip it (click the switch or the badge) and the row
  says what will happen - "Will turn OFF when you press Apply - that helps performance" - outlined in the
  colour of where it is going. Nothing changes until **APPLY**; **CLEAR** drops the flips.
- **FIX ALL HURTING** flips only rows marked safe. "Use with care" rows (Bluetooth, controllers, the
  keyboard's media keys) and security trade-offs are never flipped for you.
- The count next to each page's title says how many rows are hurting, helping and can't tell.
- Process Lasso and LiteSynapse at sign-in read HELPING when on.

### "It doesn't recognise if it went through"

- Eight settings are commands with no readable value and always read blank. They are now read where
  Windows allows it (power plan, hibernation, Nagle, NIC power saving, DEP, HPET), read with administrator
  rights by **CHECK AS ADMIN** and at the end of every APPLY (dynamic tick, HPET, DEP, TCP auto-tuning),
  and otherwise show what LiteSynapse last set, saying so.
- A setting that needs a restart and was changed this session says "takes effect after a restart".
- Values another optimizer chose that are as good as ours count as applied (for example a priority of 2A
  hex, a mouse buffer of 20, SystemResponsiveness 0), and any low-latency power plan is recognised by what
  it sets, not by its name. Windows' own defaults for missing values are understood (Game Mode reads ON on
  a stock PC).
- Start-up entries you turned off in Task Manager - including machine-wide ones and Startup-folder
  shortcuts - read OFF.
- The Device page lists the mouse and keyboard without admin rights, reads them back in the same admin
  run that changes them, and has CHECK AS ADMIN.
- The result line under the buttons names what did **not** change first, then the restart notice.

### Your CPU (Ryzen 9 7950X3D)

AMD's V-Cache driver keeps games on the cache chiplet by parking the other one, which needs Windows'
**Balanced** plan with core parking, **Game Mode ON**, and Xbox Game Bar recognising the game. So on this PC:

- **Low-latency power plan** helps **OFF**. A plan that turns core parking off - LiteSynapse's, Ultimate or
  High performance, and most optimizer plans - reads ON · HURTING. If your optimizer set one, switching this
  row OFF puts Balanced back. (USB selective suspend can still be turned off on the Device page.)
- **Game DVR** is left out of FIX ALL; if CS2 ever seems to run on the wrong cores, turn it back on.

### Rows that changed meaning

- **Priority separation** at the Windows default reads ON · HELPING: stock Windows already favours the
  program in front, and 26 hex schedules the same as the default 2. It is one-way: there is nothing worse
  to switch it to.
- **0.5 ms timer** and **Nagle off** are "use with care" now, so FIX ALL leaves them alone. The timer is
  PC-wide (more wake-ups, more power); Nagle only touches TCP, and CS2's game traffic is UDP.
- **Services** say when a disabled service is still running until the next restart, and an on-demand
  service that is not running says so instead of reading as a problem. Defender, System Guard and
  Hyper-V host services are "use with care".
- Power plan OFF puts Balanced back (not the plan you had before), and NIC power saving ON goes back to
  the driver defaults. Both rows say so.

### Fixed from the whole-app audit

- **Tray panel:** closing it no longer forces a full memory clean-up (a blocking pause) when you click
  straight back into CS2. Memory is released only after the main window closes and never with a game in front.
- **Input:** the first Snap Tap or remap of a session no longer builds code on the hook thread; a Naga
  side button held while alt-tabbing in no longer sticks; Print Screen and Pause send the right key; the
  key-capture box records right-hand Shift/Ctrl/Alt correctly. Only the input threads opt out of power
  throttling now, not the whole app.
- **Devices:** DPI change then revert could leave a game running the old DPI from a stale slot record.
  Game start no longer re-sends lighting already in the slot, a refused lighting zone is not retried, and
  a pre-write stops if a game comes to the front. Resume re-syncs once, not twice. Keyboard brightness goes
  through the device worker, and alt-tabbing out for a moment no longer re-sends keyboard lighting.
- **Files:** settings, profiles, games and onboard slot records are written atomically with a backup;
  a corrupt file is kept aside and the backup loaded, instead of your games list being replaced with defaults.
  "Switch profiles automatically" is saved. RELOAD CONFIG reloads settings too. Program names typed under
  Edit programs are cleaned up ("cs2" becomes cs2.exe; a full path becomes the file name).
- **Leaks:** window and calibration icons, tooltips, dropdowns and menus are freed; process handles are
  disposed; the log view reads only the last 64 KB and only when the Settings page is visible.
- **Tray:** the icon retries if Explorer was not ready at start-up; double-click opens the window
  instead of flashing the panel; Exit closes the window cleanly. Closing calibration early keeps your
  old calibration.
- **Window:** the BlackWidow page no longer shows the wrong profile as active; a disabled switch looks
  disabled; clicking an open dropdown closes it.
- The elevated script no longer fails when a big selection exceeds Windows' command-line limit, and
  PowerShell quoting escapes curly apostrophes (a user name like O'Brien, or a crafted file name).

### Recommendation: turn on Competitive safe

Settings > **Competitive safe** is off by default. On official Valve servers or FACEIT, turn it on: it
keeps Snap Tap and macros out of anti-cheat games. Leave it off only for offline practice.

Nothing here was seen on a real screen before shipping (it was built on Linux). A screenshot of anything
clipped or wrong is the fastest fix.

## Build 14 (1.2.3): latency audit, bug check, modern look

**What to check first:** the window looks different (below), and nothing about play should feel different
at all. If anything about input timing changed, that is a bug: say so.

- **Latency audit of the input path.** Injected keys no longer make a win32k round trip each for their
  scan code (cached per key); Snap Tap is six flags instead of three hash sets; the hook-path lists are
  sized up front; tiered compilation is off, so there is no JIT burst a few seconds into the first match.
  No timer runs while a game is in front, and the window's full memory clean-up is skipped when it closes
  because a game came to the front.
- **Settings: two switches were hidden.** "Close this window when a game starts" was cut in half and
  "Competitive safe" was below the bottom of its card. Both are visible now. Check they are there.
- **GDI leak fixed.** The macro list and two badges made a font every time they painted and never gave it
  back. Every paint-time font is now made once and reused.
- **Maximized is remembered** even when you maximize after opening and then close from the taskbar.
- **Modern look**, all drawn by the same code as before, so no new cost per frame and no animation timers:
  - Windows 11: rounded window corners, and a title bar and border in the app's own dark colours instead
    of the system accent. Windows 10 keeps the dark title bar and ignores the rest.
  - Cards are rounded with a hairline edge; the two home cards light their edge under the pointer.
  - Buttons are filled surfaces with a hairline edge; toggles are Windows 11 style (outlined when off).
  - The selected top tab has a short green bar along its bottom instead of a green box around it.
  - Rounded drop-downs with a thin chevron, rounded choice buttons (the chosen one tinted green),
    rounded checkboxes with a tick on the Performance tab.
  - The tray flyout has rounded corners on Windows 11; dialogs get the dark title bar.

None of the look was seen on a real screen before shipping (it was built on Linux). If anything is clipped,
misaligned or hard to read, a screenshot of it is the fastest fix.

## Build 13 (1.2.2)

Build 13 (1.2.2) is the first build with **no WebView2**. The window is WinForms throughout, which is the
change to look at first: if frame times in CS2 were worse with the window open before, they should not be now,
because nothing renders a browser any more.

## What changed in this build

- **WebView2 is gone.** The bridge layer, the HTML window and the WebView2 package are all removed, so the
  app no longer starts browser processes or keeps a browser cache. The exe is ~1.9 MB and the installer no
  longer looks for the Edge runtime.
- **New Performance tab** with five pages, each a list of things to tick:
  - **Razer** - what Razer's software left behind, plus the three elevated clean-up steps.
  - **Device** - whether Windows may power the mouse and keyboard down.
  - **Windows** - 34 settings, each with its own checkbox, its source, and what it costs.
  - **Services** - Windows services worth turning off, and turning back on.
  - **Start-up** - what starts with Windows, what holds a connection, and a Defender scan.
- **Every setting goes both ways.** There is no "apply everything" and no "put it all back": tick what you
  want and press "Turn ticked on" or "Turn ticked off". Each row shows the state the PC is in now.
- **Settings come from named sources** - FR33THY's Ultimate guide, Chris Titus's WinUtil, or Microsoft's own
  documentation - and the row says which. Where a guide and the Windows default disagree, the guide's value is
  offered as an option rather than called harmful; where it gives up security, the row says exactly what.

## Three bugs from the 1.1.9 logs, fixed

1. `device tuning: 0 device key(s)` - the USB power settings never reached the mouse. The old code walked
   `HKLM\SYSTEM\CurrentControlSet\Enum\USB`, whose ACL grants read to SYSTEM only, so an administrator got
   nothing and the loop silently found no devices. It now uses WMI (`MSPower_DeviceEnable` /
   `MSPower_DeviceWakeEnable`), which is what Device Manager's Power Management tab uses.
2. `razer check (tasks): Security_RegistryPermission` on every check - the scheduled-task walk called
   `GetSubKeyNames()` outside its try, so one branch of the task cache that only SYSTEM may list aborted the
   whole walk. Unreadable branches are skipped now, and the readable ones are reported.
3. `system scan: start-up "" turned off` - a Run key's own unnamed default value was being listed as a
   start-up entry. It is skipped.

## Also in this build (found by reviewing the new code, not reported)

- The risky-change confirmation was backwards for services: the green "turn ticked off" button disabled
  Defender, the firewall or Windows Update with no confirmation, while turning them *back on* warned about
  giving up security. Which direction costs something is now decided per row.
- The settings list could not be scrolled to its last two rows until the window was resized.
- Any mouse button ticked a row, so a reflex right-click selected one silently.
- The mouse wheel stopped scrolling the list after the first tab switch.
- Several steps reported a failure for something that worked: a `bcdedit /deletevalue` on a value that was
  never set, a `powercfg /delete` on a plan that is not there, and any .NET call that followed an
  `sc.exe stop` on an already-stopped service.
- `KeyboardDelay` was being written as a DWord; Windows ships that value as `REG_SZ`, so the setting was
  being ignored.

## Round 3 (the code that writes to your registry as administrator)

- **Id routing.** A start-up entry's id is a Run value name, which any program can choose without admin
  rights. A value named `accel` made "Pointer acceleration off" report "already like that" without applying
  it; one named `Spooler` made ticking that service disable a start-up program you never ticked. The kind of
  thing now travels with the id.
- **The power plan set three timeouts on the wrong plan.** `powercfg /change` edits whichever scheme is
  *active*, and the activate step ran last - so on a first run the monitor, standby and disk timeouts landed
  on Balanced. Your Balanced plan was left unable to sleep with nothing to put it back.
- A script that timed out or never started was reported as complete success.
- "Network throttling off" could never read back as applied (the write narrowed 0xFFFFFFFF to -1, the read
  did not).
- "Pointer speed at the middle notch" never took effect immediately: the two arguments to
  `SPI_SETMOUSESPEED` were in the wrong slots, so it asked for speed 0.
- `Disable-MMAgent` failures were swallowed and counted as applied.
- A registry delete that Windows *refused* was counted as success.
- HKCU values inside admin-only settings were written by the elevated process, so on a standard-user account
  they landed in the elevating administrator's profile.
- Five per-user services (OneSyncSvc and friends) cannot be stopped by name; their rows now say the change
  takes effect at the next sign-in.
- The Auto HDR preference was being wiped: `DirectXUserGlobalSettings` is one value holding a list Windows
  also writes to, and it was being overwritten wholesale.
- The Razer scheduled-task walk guessed "enabled" from a value that is not that flag, so some tasks were
  silently dropped from the clean-up.
- One unreadable Run value discarded the entire start-up list.

## Three things the WebView2 removal had dropped, now back

- **The window closes again when a linked game comes to the front.** This is the one that matters: a window
  on top of a game costs frames. Settings has the switch.
- **Window size and maximized state are remembered**, scaled for the display and clamped to the work area.
- **"Competitive safe" is a toggle again.** It was still gating macros and Snap Tap but had no UI at all, so
  it could only be changed by hand-editing `settings.json`.

The accent colour is deliberately not coming back - the window has one theme now.

## Checks to run on the hardware

1. Open the window, switch through every tab, and watch frame times in CS2 with the window open. This is the
   whole point of the build.
2. Performance > Device: both devices should be listed by name. Tick them, press "Keep ticked awake", and
   confirm in Device Manager that "Allow the computer to turn off this device to save power" is now clear.
3. Performance > Windows: tick "Pointer acceleration off" and press "Turn ticked on" - this one needs no UAC
   prompt. Then tick it again and press "Turn ticked off" and check the Windows mouse dialog follows both ways.
4. Performance > Services: turn one safe service off (DiagTrack), confirm it in services.msc, then turn it
   back on and confirm it returns to Automatic and running.
5. Performance > Start-up: the list should match Task Manager's Startup apps, and turning one off should show
   as Disabled there.
6. Anything in the Security trade-offs group should ask you to confirm by name before it applies - and on
   the Services page, the confirmation should appear when turning something **off**, not on.
7. Performance > Windows: scroll to the very bottom of the list. The last rows of the last group should be
   reachable without resizing the window. Hover the list and use the wheel after switching pills a few times.
8. Start a linked game (CS2) with the window open: the window should close by itself. Then turn
   "Close this window when a game starts" off in Settings and check it stays open.
9. Resize the window, close it, reopen it: it should come back the size you left it.

## Windows limits your network while a game runs

This is the big one and it is on by default on every Windows PC.

While any multimedia app is running (a game counts), Windows' multimedia scheduler **limits all other network
traffic to about 10 packets per millisecond**, and **keeps 20% of the CPU back** for background work. Two
registry values under `Multimedia\SystemProfile` control it: `NetworkThrottlingIndex` and
`SystemResponsiveness`. Both are documented by Microsoft, and both are in **Settings → Performance → Device
latency** now:

| Row | Default | What LiteSynapse sets |
|---|---|---|
| Network limit while gaming | ~10 packets/ms | Off |
| CPU kept for background work | 20% | 10% |

"Set up for lowest latency" now does these two as well as the mouse ones. This is the setting most likely to
explain a game feeling laggy while something else is using the connection.

## Harmful settings from other tools

New card in **Settings → System scan**. Optimizer guides and YouTube scripts leave settings behind that make a
PC *worse*. LiteSynapse finds them and puts the Windows default back. It never sets any of them.

| Found | Why it is bad |
|---|---|
| CPU security fixes turned off | Spectre/Meltdown protection off for a few frames. Any program can read memory it should not, passwords included. |
| User Account Control turned off | Nothing asks permission before changing the PC. Gains no performance at all. |
| Thread priority changed | `Win32PrioritySeparation` set to 26, 38 and the like. Usually makes frame pacing worse. |
| Mouse or keyboard queue size changed | Smaller buffers do not lower latency, and they drop input when the PC is busy. |
| Global timer resolution forced | Gives every program the 1 ms timer, raising interrupts and power use system-wide. |
| Desktop compositor test mode | An internal debug switch left on in the window compositor. |
| csrss.exe priority changed | Raising a core Windows process' priority starves the rest of Windows. Known cause of stutter. |

The repair also clears the boot-time overrides these guides love — `useplatformclock`, `useplatformtick`,
`disabledynamictick`, `tscsyncpolicy`. Modern CPUs keep better time on their own; forcing these makes timing
worse. They need administrator rights even to read, so they are not listed, but the repair clears them
whenever you tick anything.

Every entry says what it does to your PC now and exactly what it will be set back to. Nothing is deleted and
no new tweak is applied; each step restores a Windows default.

## Install

1. Run `LiteSynapse-Setup-1.1.9.exe`. It keeps your profiles, games and macros.
2. Quit Razer Synapse fully.

## Checklist

| # | Try this | Expected |
|---|---|---|
| 1 | **Settings → Performance → Check now** | Two new rows: network limit and CPU kept for background work |
| 2 | **Set up for lowest latency** | The dialog now lists those two as well |
| 3 | Afterwards, re-check | Both read Off / 10% |
| 4 | **Settings → System scan → Scan now** | The "Harmful settings from other tools" card appears |
| 5 | If it finds nothing | Good — nothing has messed with your PC. The card says so. |
| 6 | If it finds something | Read the reason, tick it, **Repair selected**, restart |
| 7 | Everything from build 9 | Unchanged |

**Don't test Snap Tap or macros in Valve matchmaking or FACEIT.**

---

# LiteSynapse 1.1.8 test build 9: System scan

New tab: **Settings → System scan**. It looks at everything that starts with Windows, the background services
worth turning off, and what is using your connection — and lets you switch things off from one list.

## First, what it is not

**LiteSynapse is not an antivirus and never calls a file a virus.** A homemade "this looks like malware"
guess is worse than useless: it is wrong often enough to make you switch off something you needed, and it
makes you feel covered when you are not.

So the scan reports **facts** and hands the verdict to the real thing:

- where a program lives
- whether it carries a digital signature, and whose name is on it
- whether it starts itself

Three of those facts make something **worth a look**, shown with an orange tag:

| Tag | What it means |
|---|---|
| Starts from a temporary or downloads folder | Normal programs do not do this. Very common for things you did not install on purpose. |
| Carries no digital signature | Windows cannot tell you who made it. |
| Its program file is gone | A leftover entry that does nothing. |

Every flagged item gets a **Check** button that runs a real **Microsoft Defender** scan of that exact file and
tells you what Defender said. Defender decides; LiteSynapse does not.

The card also shows whether Defender's real-time protection is on.

## Network, upload and download

- **Windows Update uploads.** Windows ships with Delivery Optimization sharing updates with other PCs — on
  your network, and in some settings **with strangers on the internet**. It uses your upload, which is what
  makes a game feel laggy while Windows is busy. The scan shows the mode in plain words and can set it to
  "download from Microsoft only, upload nothing".
- **What is on the network right now.** Anything holding a connection gets a **network** tag, so you can see
  which of your start-up programs is actually talking.
- **Known background uploaders** are listed by name: Google and Adobe updaters, NVIDIA telemetry and
  container, Steam, Epic, Dropbox, OneDrive, Google Drive, Creative Cloud, Discord, Razer.

## What it can turn off

Only two kinds of things, and nothing else is ever offered:

- **Start-up entries for your account.** Turned off exactly the way Task Manager's Startup apps does. No
  administrator prompt, and you can turn them back on in Task Manager.
- **Services from a fixed allowlist**: telemetry (DiagTrack, dmwappushservice), SysMain, Windows Search, the
  three Xbox services and the Print Spooler. Each row says what it costs you *and what you give up* — the
  Xbox ones break Game Pass, the Spooler breaks printing, and the list says so before you tick it. They are
  set to Disabled, not removed; services.msc puts them back.

Anything not on that allowlist is not shown a button, so the scan cannot talk you into breaking Windows.
There is a test that asserts the allowlist never contains Defender, the firewall, networking, audio, event
log, Windows Update or the HID stack.

## Install

1. Run `LiteSynapse-Setup-1.1.8.exe`. It keeps your profiles, games and macros.
   - SmartScreen: **More info → Run anyway**.
2. Quit Razer Synapse fully.
3. Click **Finish** with "Launch" ticked.

## Checklist

| # | Try this | Expected |
|---|---|---|
| 1 | **Settings → System scan → Scan now** | A second or so, then the lists fill in |
| 2 | Read "Starts with Windows" | Matches Task Manager's Startup apps, with publisher and path added |
| 3 | Anything tagged **Worth a look** | Read why. Press **Check** to have Defender scan that file |
| 4 | Read "Windows Update uploads" | Says whether Windows is uploading updates to other PCs |
| 5 | Tick a start-up entry you don't want, then **Turn off selected** | Lists what you lose, then turns it off with no UAC prompt |
| 6 | Open Task Manager → Startup apps | That entry shows as Disabled |
| 7 | Tick a service (e.g. telemetry) and the Windows Update uploads box | One UAC prompt, then both are off |
| 8 | **Scan again** | The changes show up |
| 9 | Everything from build 8 | Unchanged |

**Don't test Snap Tap or macros in Valve matchmaking or FACEIT.**

## Undoing anything

- Start-up entries: Task Manager → Startup apps → Enable.
- Services: services.msc → the service → Startup type → Automatic or Manual → Start.
- Windows Update uploads: Settings → Windows Update → Advanced options → Delivery Optimization.

**Settings → Open config folder** opens `%LOCALAPPDATA%\Programs\LiteSynapse\data`. Send me `log.txt`.

---

# LiteSynapse 1.1.7 test build 8: Razer off for good, and the mouse at its fastest

Settings → Performance now has two cards: **Device latency** (new) and **Razer leftovers** (wider).

## Device latency (new card)

It checks the five Windows settings that put delay between your hand and the game, and one button fixes
all of them:

- **Background polling cap.** Windows throttles raw mouse input to **125 Hz** for any window that is not in
  front. An overlay, a second monitor or a moment of lost focus and your 1000 Hz mouse is sending a quarter
  of its reports. LiteSynapse turns the cap off (`RawMouseThrottleEnabled`).
- **Mouse acceleration** ("Enhance pointer precision"): the same hand movement travels a different distance
  depending on how fast you move it, so aim is not repeatable. Turned off.
- **Pointer speed.** Anything but the middle notch (6 of 11) makes Windows scale the mouse's counts, which
  throws movement away or doubles it. Put back to the middle.
- **USB power saving** on the Naga and the BlackWidow: Windows may put them to sleep, and the first movement
  after a quiet moment waits for the device to wake. Turned off per device.
- **USB selective suspend** in the power plan: the same thing, plan-wide. Turned off.

The first three are settings of your Windows account and apply at once, no administrator prompt. The two USB
ones ask for administrator permission once. Everything can be set back in Windows' own Mouse settings, Device
Manager and Power Options; the dialog lists exactly what it will change before you agree.

Two of your own notes are already covered: maximum polling rate is on the Mouse → Performance page, and the
DPI advice (400 at 1080p, 800 at 1440p, 1600 at 4K) is your call per profile.

## Razer leftovers (wider)

"Turn off Razer services" now covers everything Razer leaves running, not just part of it:

- **Any Razer service**, found three ways: its program sits in a Razer folder, or it is named `Razer…`, or
  it is the CPUID driver. That picks up Chroma SDK Server and Service, Razer Central, the Synapse service,
  RazerExperienceService, the elevation service, haptics — wherever they were installed.
- **Network.** Services known to listen (Chroma SDK, Central, Synapse, updaters) are marked **network** in the
  list. A new row counts the **Windows Firewall rules** that let a Razer program through, and the clean-up
  removes them. After that, nothing Razer can reach the network or accept a connection even if it comes back.
- **Every Razer scheduled task**, not only the upgrade one. They are found by reading the task cache, so
  nothing is started to look for them.

## Also in this build

The optimization work from build 7 is in here too: one command to the mouse per game switch, repeated
foreground events cost nothing, device work below your game's threads, and memory given back when the window
closes.

## Install

1. Run `LiteSynapse-Setup-1.1.7.exe`. It keeps your profiles, games and macros.
   - SmartScreen: **More info → Run anyway**.
2. Quit Razer Synapse fully (tray icon → **Quit**).
3. Click **Finish** with "Launch" ticked.

## Checklist

| # | Try this | Expected |
|---|---|---|
| 1 | **Settings → Performance → Check now** | Both cards fill in |
| 2 | Read the **Device latency** rows | They match what Windows actually has (check Mouse settings and Device Manager) |
| 3 | **Set up for lowest latency** | Lists what it will change, one UAC prompt, then reports what is off |
| 4 | Afterwards: Windows Mouse settings | "Enhance pointer precision" off, speed at the middle notch |
| 5 | Afterwards: Device Manager → the Naga → Power Management | "Allow the computer to turn off this device" unticked |
| 6 | Look at **Other Razer services and tasks** | Lists the services with a **network** tag on the ones that listen, plus any Razer scheduled task |
| 7 | **Turn off Razer services** | Disables them all, disables the tasks, removes the Razer firewall rules |
| 8 | Re-check | Everything reads Off / Blocked / None |
| 9 | Sign out and back in, then play | Report whether it feels different |

**Don't test Snap Tap or macros in Valve matchmaking or FACEIT.**

## If something goes wrong

Everything here is reversible in Windows:
- Mouse settings: Settings → Bluetooth & devices → Mouse → Additional mouse settings → Pointer Options.
- Device sleep: Device Manager → the device → Power Management.
- USB selective suspend: Power Options → Change plan settings → Advanced → USB settings.
- Razer services: services.msc (set back to Automatic or Manual), Task Scheduler, and Razer's installer
  re-creates its firewall rules.

**Settings → Open config folder** opens `%LOCALAPPDATA%\Programs\LiteSynapse\data`. Send me `log.txt`.

---

# LiteSynapse 1.1.6 test build 7: the optimization pass

Build 7 is about doing less: fewer commands to the mouse, less work when a game comes to the front, and less
memory held while you play. Nothing about how you use it changed.

## Fewer commands to the mouse

Switching into a game used to send up to nine commands to the Naga right as the game took the foreground
(read the slot, switch it, set DPI, set the polling rate, then six lighting commands). It now sends **one**:
the slot switch.

- The slot already stores its own DPI, and the slot LiteSynapse switches to was written with this profile, so
  the DPI command is not needed. LiteSynapse just remembers the value.
- The polling rate is not part of a slot (its command carries no slot number), so a slot switch cannot change
  it and it is no longer re-sent afterwards.
- The lighting is already in the slot too (build 6 stopped re-sending it after a switch).

So an alt-tab into CS2 and back is about 10 ms of USB traffic instead of about 90 ms, and none of it lands
while the game is loading its first frames.

## Less work while a game runs

- **Repeated foreground events cost nothing.** Windows sends "a new window is in front" again for the same
  window (a game re-focusing itself, an overlay closing). LiteSynapse now recognizes that with two cheap calls
  and stops there, instead of opening the game's process, reading its path and matching it against your 47
  rules every time.
- **Device work runs below your game.** The thread that talks to the mouse and keyboard was at normal
  priority; it is now below normal. It waits on USB rather than using the CPU, so it can never take time from
  a frame.
- **The tray tooltip is only rewritten when its text really changes** (it used to be rewritten on every
  change the window made, including dragging a lighting slider).

## Less at start-up and less memory

- The Razer leftovers check used to walk every Windows service and start `schtasks.exe` five seconds after
  sign-in, which is exactly when you might be launching a game. That part now waits until you open the
  Settings → Performance page. The quick checks still run at start-up, so the warning dot still appears.
- When the last window closes, LiteSynapse gives its memory back to Windows instead of holding what the
  window needed for the rest of the session. It skips this while a game profile is live, so it can never
  pause anything.
- The app is built with the workstation garbage collector, no ETW plumbing and no hot-reload support: a
  smaller, quieter process.

**At rest LiteSynapse wakes for nothing.** Every thread waits on an event: no polling loop, no timer, no
periodic check. It runs only when a window comes to the front, a key is pressed with a profile live, a device
is plugged in, or you use the window.

## Install

1. Run `LiteSynapse-Setup-1.1.6.exe`. It closes the running LiteSynapse, replaces the old build and keeps your
   profiles, games and macros.
   - SmartScreen says "Windows protected your PC" because the app isn't code-signed: **More info → Run anyway**.
2. Quit Razer Synapse fully (tray icon → **Quit**).
3. Click **Finish** with "Launch" ticked.

## Checklist

| # | Try this | Expected |
|---|---|---|
| 1 | Start CS2, alt-tab out and back a few times | The profile follows; the log shows one "live slot" line per switch and no "mouse dpi" lines |
| 2 | Check the log after a game switch | No DPI or polling line, just the slot switch |
| 3 | **Mouse → Performance**: change DPI | Takes effect right away (this path still sends it) |
| 4 | Open Task Manager, watch LiteSynapse while playing | 0% CPU except at the moment of a switch |
| 5 | Open the window, close it, look at the memory column | It drops back after closing |
| 6 | **Settings → Performance → Check now** | Still finds the services, the task and the autostart |
| 7 | Everything from build 6 | Unchanged |

**Don't test Snap Tap or macros in Valve matchmaking or FACEIT.**

## If something goes wrong

**Settings → Open config folder** opens `%LOCALAPPDATA%\Programs\LiteSynapse\data`. Send me `log.txt`.

---

# LiteSynapse 1.1.5 test build 6: what to try

Build 6 is the bug-test build: two full review rounds over the whole app (CS2 and anti-cheat, latency,
Windows, the mouse protocol, threading, the window), each finding checked a second time before it was fixed.
Nothing here changes how you use LiteSynapse; it fixes things that could have bitten you.

## What changed

**CS2 and anti-cheat**
- Competitive-safe now recognizes a game by its program (`cs2.exe`, VALORANT, Apex, ...), not only by the
  profile name. Renaming "CS2", linking `cs2.exe` to another profile, or not linking it at all no longer
  turns the protection off.
- Deadlock, League of Legends and GTA count as anti-cheat games now (VAC, Vanguard, BattlEye).
- A macro that is still running stops the moment you switch profiles, alt-tab into a protected game or exit
  LiteSynapse, and it releases the keys it was holding, so nothing is left pressed.
- A mouse button set to middle click no longer stays stuck down when the profile changes while you hold it.

**Windows**
- Double-clicking the LiteSynapse shortcut while it runs in the tray opens the window. It used to do nothing.
- Starting LiteSynapse while an administrator copy is running no longer crashes silently.
- Windows updates, Setup and sign-out now close LiteSynapse properly: no leftover process, no ghost tray icon.
- Run as administrator keeps its tray icon after Explorer restarts.
- The clean-up steps start Windows PowerShell by its full path.

**Mouse**
- Slots 1 and 2 are LiteSynapse's in both modes; your own profiles go to slots 3, 4 and 5. Before, the window
  let you write slot 2 and then moved the mouse off it.
- Alt-tabbing in and out of a game no longer rewrites the lighting each time.
- Writing a slot the mouse is running now updates DPI and lighting right away.
- If Razer Synapse is running and answers on the same mouse, LiteSynapse no longer mistakes its answer for its own.
- DPI and polling changed in the classic window now take the same path as the new window.
- The self-test pauses LiteSynapse's own mouse writes while it runs and puts your profile back afterwards.
- The classic window no longer reads the mouse and keyboard, or the whole log, on every profile switch.

## Install

1. Run `LiteSynapse-Setup-1.1.5.exe`. It closes the running LiteSynapse, replaces the old build and keeps your
   profiles, games and macros. No uninstall needed.
   - SmartScreen says "Windows protected your PC" because the app isn't code-signed: **More info → Run anyway**.
2. Quit Razer Synapse fully (tray icon → **Quit**) so it can't fight over the mouse and keyboard.
3. Click **Finish** with "Launch" ticked.

## Checklist

| # | Try this | Expected |
|---|---|---|
| 1 | Open LiteSynapse, then double-click the desktop shortcut | The window comes to the front (before: nothing happened) |
| 2 | **Mouse → Onboard memory** | Slots 1 and 2 show as LiteSynapse's; only 3, 4 and 5 can be written |
| 3 | **Mouse → Customize**: remap a side button, try it in Notepad | The button sends the new key about a second later |
| 4 | **Mouse → Lighting**: pick a zone and a color | Only that zone changes |
| 5 | **Settings → Gaming**: turn Competitive-safe on, start CS2 | The log says "competitive-safe: cs2.exe is anti-cheat protected" and no Snap Tap or macros run |
| 6 | Rename your CS2 profile, start CS2 again | Still protected (this is the main fix) |
| 7 | Start a game, alt-tab out and back a few times | The mouse switches once each way; the log shows no repeated lighting writes |
| 8 | **Settings → Performance**: Check now, then **Turn off Razer services** | Lists what it will turn off, asks for administrator permission once, and reports what is off |
| 9 | **Settings → Maintenance → Run self-test** | It runs, and afterwards the mouse is back on your profile |
| 10 | Sidebar → **Exit** | Window and tray icon go away; the mouse is back on slot 3 (green) |

**Don't test Snap Tap or macros in Valve matchmaking or FACEIT.** Valve kicks for Snap Tap-style input and one-key
multi-action binds on its official servers. Use a practice or offline server.

## If something goes wrong

**Settings → Open config folder** opens `%LOCALAPPDATA%\Programs\LiteSynapse\data`. Send me `log.txt` from there.

---

# LiteSynapse 1.1.4 test build 5: what to try

New in build 5: **lighting per zone**. **Mouse → Lighting** has a **Zone** row: All, Scroll wheel, Logo, Side panel.

- Pick a zone, then an effect, color and brightness: only that zone changes. A green dot marks zones with their
  own lighting, and **Same as the rest** puts a zone back in line with the others.
- **All** sets every zone the same (zones with their own lighting follow it again).
- Each zone is saved with the profile and written into the mouse's live slot, like everything else.
- The lighting is no longer marked Experimental: you confirmed colors change on the mouse.

| # | Try this | Expected |
|---|---|---|
| 1 | Zone **Logo** → red | Only the logo turns red |
| 2 | Zone **Scroll wheel** → Breathing, blue | Only the wheel breathes blue |
| 3 | Zone **All** → green | Every zone is green again |
| 4 | Switch to another profile and back | Each profile keeps its own zone colors |

Everything from build 4 below still applies.

---

# LiteSynapse 1.1.3 test build 4: what to try

Build 4 fixes what your build 2 and 3 logs showed:

- **Your own slots stay untouched.** Build 3 kept running from slot 3 when it held the same profile, and color
  clicks were saved into it. Now LiteSynapse always runs from its own slots 1 and 2 and puts the mouse back on
  your slot when you exit.
- **Warns when Razer Synapse is running.** Synapse can change the lights and switch slots behind LiteSynapse's
  back. If it's running, a yellow banner says so on Overview, Mouse and Keyboard. Quit it from its tray icon.
- **Classic window switches instead of stacking.** Opening the classic window closes the new one, and the tray's
  "LiteSynapse" switches back.
- Quieter log: no "0F/84 failed" lines from Detect zones, Razer checks logged only when something changed.

Everything from build 3 below still applies.

## Install

1. Run `LiteSynapse-Setup-1.1.4.exe`. It closes the running LiteSynapse, replaces the old build and keeps your
   profiles, games and macros. No uninstall needed.
   - SmartScreen says "Windows protected your PC" because the app isn't code-signed: **More info → Run anyway**.
2. Quit Razer Synapse fully (tray icon → **Quit**) so it can't fight over the mouse and keyboard.
3. Click **Finish** with "Launch" ticked.

## What changed in build 3

- **Buttons are what they are, and you remap them.** No button calibration anymore: the Calibration tab is gone
  and **Mouse → Buttons** is now **Customize**, like Synapse.
  - A button you haven't remapped shows its factory default: side buttons 1–12 send 1–9, 0, -, = and the wheel
    click is middle click. These now really get written to the mouse, so **Reset to default** works on the mouse.
    Before, a slot could keep another profile's key on an unmapped button.
  - The wheel tilts and the two buttons behind the wheel have no known factory code yet. If you leave one of
    them unmapped, it keeps whatever the mouse had. Your profiles all map them, so this rarely matters.
- **Exit button** at the bottom of the sidebar, like Synapse's Exit. Closing the window leaves LiteSynapse running
  in the tray. **Exit** closes it completely, and the mouse goes back to its own onboard slot (3, green).
- **Lighting pages** say what to check if the lights don't change: Razer Synapse or Windows Dynamic Lighting can
  take over the lights (Synapse warns about the same thing).
- **New logo** (your round "RzR optimizer" badge, background removed) on the tray icon, the taskbar, the
  installer and the sidebar. The tray uses a simplified version because the text is too small to read at that size.

## From build 2 (still applies)

- The mouse runs the profile LiteSynapse shows. On the desktop that's your starting profile; in a linked game,
  the game's profile; after you click a profile, that one. Remaps, DPI, polling and lighting apply right away.
- A profile you click turns auto-switch off. Its keyboard macros and Snap Tap then work everywhere, except
  inside the LiteSynapse window.
- Keyboard lighting is Off, Static or Breathing, the only effects the BlackWidow Lite has.

## Checklist

| # | Try this | Expected |
|---|---|---|
| 1 | Look at the tray icon and the taskbar | The new round logo |
| 2 | Open LiteSynapse | Overview shows your starting profile running on the mouse ("… is running on the mouse (slot 1 or 2)") |
| 3 | **Mouse → Customize**: click a key name, pick a key | The label turns green. About a second later the button sends the new key (try it in Notepad) |
| 4 | **Mouse → Customize**, side view: pick side button 8, then **Reset to default** | It shows 8, and in Notepad the button types 8 |
| 5 | **Mouse → Performance**: change DPI and polling rate | The mouse speed changes right away |
| 6 | **Mouse → Lighting**: pick a static color | Tell me which zones change: scroll wheel, logo, side panel |
| 7 | **Keyboard → Lighting**: Off, Static, Breathing and the brightness slider | The backlight follows |
| 8 | Click a keyboard profile in the profile switcher, open Notepad, press an arrow key that has a macro | The macro runs (in Notepad, not in the LiteSynapse window) |
| 9 | Turn auto-switch back on, start CS2 | Within about a second the mouse switches to your CS2 profile |
| 10 | Alt-tab out of CS2 and back quickly (under 3 s) | The mouse doesn't switch back and forth |
| 11 | Sidebar → **Exit** → Exit | The window and tray icon go away and the mouse is back on slot 3 (green) |

**Don't test Snap Tap or macros in Valve matchmaking or FACEIT.** Valve kicks for Snap Tap-style input and one-key
multi-action binds on its official servers. Use a practice or offline server.

## If something goes wrong

**Settings → Open config folder** opens `%LOCALAPPDATA%\Programs\LiteSynapse\data`. Send me `log.txt` from there.
It shows each write to the mouse ("live slot …", "mouse lighting …", "mouse now: dpi …", "exit requested …").
