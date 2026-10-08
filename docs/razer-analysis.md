# LiteSynapse and your Razer software: what we found, what we will change
Date 2026-10-08. Devices: Razer Naga Left-Handed Edition (1532:008D) and BlackWidow Lite (1532:0235). Sources: copies of your Razer program files (Synapse/RazerAppEngine 4.0.699/821/823/827, Razer Game Manager Service 3, three Razer driver packages), your two Synapse profile exports, and LiteSynapse source and data (read-only, commit f9b59cc). A separate verifier re-checked every finding; its corrections are listed under "Technical notes".

## PART 1: Plain-language summary

### Your question: can you turn off your PC and continue on your phone?
YES. This work runs on Anthropic's cloud computers, not on your PC. The session reports the cloud, and it keeps going after your PC is off. On your phone, open the Claude app (or claude.ai/code) and pick this same session. The "on your PC" steps below (testing the real mouse and keyboard, an optional Synapse recording, removing Razer leftovers) can wait until you are back.

### What we learned
1. Razer's game list is now readable. Razer's Game Manager service keeps an encrypted list of about 3,600 games and their program names. We decrypted it, and it helped us link more of your profiles:
   - Embr runs as FireProject.exe, not Embr.exe, so your Embr profile probably never switched on.
   - "zomb" is very likely Project Zomboid.
   - Dead Rising has six known program names.
   - Call of Duty, The Last of Us and Halo each have extra program names we were missing.
2. Razer's Game Manager service costs performance. It starts with Windows, runs with full system rights, and watches every program and DLL start on the PC through a kernel event feed. When a game launches, it can spend seconds of CPU re-reading its whole game list. LiteSynapse does not need it.
3. Razer's drivers sit in the path of every mouse and keyboard signal. Even with Synapse closed, every movement and key press passes through extra Razer code inside Windows. The cost is small (estimated, not measured), but it is not zero. LiteSynapse needs none of these drivers; it uses the standard Windows drivers.
4. Synapse itself is mostly a web page loaded from Razer's servers (apps.razer.com). That page decides which commands go to your mouse, so the exact Naga commands are not in your files. One built-in Synapse switch, --logProtocol, records every command it sends.
5. Your profiles came over almost perfectly. Keys, DPI and polling match on all 47 shared mouse profiles, and the CS2 keyboard profile and both jumpthrow macros match. What did NOT come over:
   - all mouse lighting colours and effects (49 profiles),
   - seven side buttons that were switched off in Synapse,
   - the middle click on Tarky side button 4,
   - two old copies, "Valorant (1)" and "Valorant (2)".
6. Snap Tap and jumpthrow macros can get you kicked in CS2. Valve's August 2024 CS2 update says that automating several actions from one key, and "hardware-assisted counter-strafing", get players kicked on Valve's official servers. Your CS2 profile uses both. This comes from press coverage of Valve's notes; Valve's own page was not reachable from here.
7. We found and tested a real bug where a key stays held. If you hold A or D while alt-tabbing back into CS2, the game can keep that key held after you release it. We reproduced it and tested a fix.
8. The mouse switches memory slots on every alt-tab. It flips between slot 1 and slot 3, and both hold the same "CS2" profile. Each switch uses slow fixed waits (about 0.2 s), and the mouse's memory sometimes gets rewritten when it does not need to be.

### What we will change in LiteSynapse (most important first)
- Fix the stuck-key bug, for Snap Tap and for the arrow-key macros.
- Add a clear warning plus a "Competitive-safe" switch for Snap Tap and macros in CS2 and other anti-cheat games. Your CS2 setup will not be changed without asking you.
- Stop the slot flipping. Rewrite the mouse's memory only when a profile really changed, and never while you are in a game.
- Remove the memory clean-up that runs when the tray panel closes. It can cause a hitch exactly as you click back into a game.
- Add the missing game links (Embr, Project Zomboid, Dead Rising, COD, TLOU, Halo and others).
- Make device commands much faster: keep the connection open and wait microseconds to a few milliseconds, instead of 20-80 ms.
- Bring back per-profile mouse lighting, the switched-off buttons, and Tarky's middle click.
- Keep keyboard handling fast and steady in games: compile ahead of time, give the input thread higher priority, opt it out of Windows power saving, avoid memory clean-up pauses during play, write the log in the background, and re-check that the keyboard hook is still active.
- Add a "Link the game that is running now" button, for the games we could not identify (dealer, paintgame, SPECTRE, Marathon).
- Add a check that tells you when Razer's Game Manager service or Razer drivers are still installed, with guided removal.

### What to do on your PC when you are back (nothing is needed right now)
1. Answer the questions below. You can do this from your phone.
2. Optional, BEFORE uninstalling Synapse: record Synapse's device commands. This gives us the exact lighting and button commands for your Naga.
   - Quit Synapse completely (tray icon, then Quit). It allows only one running copy.
   - Run: "C:\Program Files\Razer\RazerAppEngine\app-4.0.827\RazerAppEngine.exe" --logProtocol (use the app-4.0.xxx folder with the highest number on your PC).
   - On the Naga, do each of these once: set a static colour, then Spectrum; set one side button to Disabled and another to Middle click; change DPI and polling rate; switch profiles.
   - Quit Synapse, then send us the files that start with "protocol" from %LOCALAPPDATA%\Razer\RazerAppEngine\User Data\Logs\.
3. Uninstall Razer Synapse and Razer App Engine (Settings > Apps). LiteSynapse replaces them. While Synapse is installed, it keeps reinstalling Razer's drivers.
4. If "Razer Game Manager Service 3" is still there, turn it off. In an admin terminal, run:
   sc query "Razer Game Manager Service 3"
   If the service exists, then run:
   sc stop "Razer Game Manager Service 3"
   sc config "Razer Game Manager Service 3" start= disabled
5. Optional but recommended (moderate risk): remove the Razer filter drivers.
   - Before you start: make a restore point and keep a spare USB keyboard or mouse nearby. Each device drops out for a moment during removal.
   - List the packages with: pnputil /enum-drivers. Look for provider "Razer Inc" with original names rzdevu_008d_*.inf, rzdevu_0235_*.inf and rzcommonu.inf, and note each oemNN.inf name.
   - Back up each one: pnputil /export-driver oemNN.inf C:\RazerDrvBackup
   - As admin, delete each with pnputil /delete-driver oemNN.inf /uninstall, in this order: RzCommon, the two VCon packages, Mou/Kbd, then Dkm/MPos.
   - Run pnputil /scan-devices, or replug the devices. Reboot if Windows asks.
   - Never delete mshidkmdf; it belongs to Windows.
   - To undo: pnputil /add-driver "C:\RazerDrvBackup\*.inf" /subdirs /install, then replug or reboot.
   - We have a script that does these steps safely. It only lists the packages unless you add -Remove, and it backs up first. It will ship with LiteSynapse.
6. Check that the mouse and keyboard work, and that the LiteSynapse log shows "mouse dpi ...:True" and "kb brightness ...:True".
7. After any Razer software update, repeat steps 4 and 5. Razer installers re-register the service and the drivers.

### Questions for you
1. CS2: keep Snap Tap and the jumpthrow macros (risk of being kicked on Valve's official servers), or turn them off? With both off, LiteSynapse adds zero keyboard delay in CS2.
2. Which games are these profiles for?
   - "dealer": our guesses are Drug Dealer Simulator or Buckshot Roulette.
   - "paintgame": our guess is Paint the Town Red.
   - "SPECTRE": probably Spectre Divide, which was shut down in 2025.
   - "Marathon": not in any of Razer's data.
   The new "link running game" button will also solve this.
3. Should your other games use the keyboard profile "gamer - S3" (Snap Tap on, brightness 7)? Today Snap Tap is off in every game except CS2. That changed when the old "PC-Default" keyboard profile was removed.
4. Do you want "Valorant (1)" and "Valorant (2)" back? They look like old copies of Valorant with the tilt buttons swapped.

## PART 2: Technical notes (all verified; file:line refers to decompiled or prettified sources under scratchpad/analysis, or to src/LiteSynapse/LiteSynapse at f9b59cc, where lines may drift)

### GMS3 (Razer Game Manager Service 3)
- MasterGameList.xml: AES-256-CBC with PKCS7 padding and a static key/IV (IoUtil.cs:14-30). No compression on disk. Decrypted: 5,470,115 bytes, 3,583 GameSignature entries, version 3.0.3710. Output files: analysis/gms3/MasterGameList.decrypted.xml and games.json.
- Server copy: gzip over plain HTTP from deals-assets-cdn.razerzone.com/mgl/latest-20240501.xml.gz, MD5-checked against the ETag, then re-encrypted on disk.
- Anti-cheat flag: GameInfoRIG & 0x10000 (GameProcessManager.cs:37, 1587). It is set on these of your profiles: APEXLEGENDS, BF, Call of Duty, CS2, fortnite, h1z1, Halo, MarvelRivals, new world, PUBG, rogue, rust, splitgate, SIEGE, Tarky, Valorant.
- Service setup: "Razer Game Manager Service 3" is AUTO_START, LocalSystem, own process (GMSServiceRegister CreateServiceW at 0x401b57; ServiceConstant.cs:12).
- How it watches processes: a real-time kernel ETW session (TraceEventProcessMonitor.cs:20, 57-58). The bundled TraceEvent library adds Thread events (TraceEventSession.cs:858), FlushTimer is 1 s, and the buffer pool is 64 MB or more. Its loop wakes every 1 s even when idle (:303-320).
- Cost at game launch: a full decrypt and parse of the MGL for every GUID that matches the process name (GameProcessManager.cs:627-638). One parse takes 45-90 ms and allocates about 7.8 MB (benchmarked on Linux). launcher.exe matches 128 GUIDs and start_protected_game.exe 30. Note: this path also runs after a single local/user game-list match.
- Every game start and stop rewrites encrypted XML files and is broadcast over SignalR.
- Background work: an MGL check every 12 h, a full rescan when the MGL changes, and an analytics POST every 45 min to maxineapi.razersynapse.com.
- LiteSynapse does not use GMS3. It switches profiles on SetWinEventHook(EVENT_SYSTEM_FOREGROUND) (TrayHost.cs:150) plus an exe-name match (Service.cs:248).

### Synapse host (Electron)
- The host has no device-specific tables; the only vendor IDs present are 0x1532 and 0x068E. The UI and the HID payloads come from https://apps.razer.com/synapse/dashboard/ (main.js:88, 521, 2323). That site was blocked from this sandbox.
- Protocol 2.5 commands: 00/81 firmware, 00/86 edition, 00/84 get device mode, 00/04 set device mode, 03/0A LED matrix RGB effect, 0F/02 chroma effect. Status codes: 0 new, 1 busy, 2 success, 3 failed, 4 timeout, 5 not supported.
- Report offsets: a 90-byte payload has status at [0], size at [5], class at [6] and id at [7]. A 91-byte report shifts each by +1 because of the report ID byte.
- Timing and retries:
  - HID handles stay open.
  - At least 5 ms between commands (host fallback; the web app may override it).
  - For default/suspend/exit command lists and set-mode/LED effects: wait 5 ms (+3 ms per retry), read status up to 3 times (5 for setDeviceMode), resend up to 3 times. These waits are busy-wait spins.
  - Lighting batches: at least 2 ms apart, at most 2 queued, extra frames dropped.
- HID.node batch format: 0xF0 = send, 0xF1 = Sleep, 0xF2 = large send.
- timeBeginPeriod is called only on IPC request.
- Raw button events use the hidConfig collection, with report ID 5 by default.
- Every window sets backgroundThrottling:false, plus GPU switches. Timers: BLE scan every 5 s, log flush every 10 s.
- --logProtocol writes protocol.log under %LOCALAPPDATA%\Razer\RazerAppEngine\User Data\Logs (main.js:358-362). The app is single-instance (main.js:318-321).

### Kernel drivers
- RzDev_008d.sys and RzDev_0235.sys have identical code (corrected hashes b4405439...9f117a1a and 3c4ef1af...9212dd36). Both are KMDF 1.15 and demand-start.
- Naga attachments:
  - Mou: upper filter on HID MI_00, which is the 90-byte control channel.
  - Kbd: HID MI_02.
  - Dkm: lower filter on USB MI_01.
  - MPos: lower filter on USB MI_00.
  - VCon: virtual consumer device.
- BlackWidow Lite attachments: Mou on HID MI_02 (the control channel), Kbd on MI_01&Col01, Dkm on USB MI_01, VCon; no MPos.
- RzCommon.sys inserts itself into every mouse and keyboard packet at device start, even when no client is connected. Per packet it takes a spinlock, writes into a 256-entry ring and does a 0x318-byte memset, then calls mouclass while still holding the lock. Capture and remap are off by default (fail-safe). Synapse's injected media/joystick reports wait on a 20 ms timer.
- mapping_engine.dll is RzCommon's only client. Synapse reinstalls the drivers itself (main.js:2313-2315).
- LiteSynapse already uses the 91-byte feature collection on MI_00 (Naga) and MI_02 (BlackWidow Lite), with access-0 handles, and needs no Razer driver.

### Profiles
- The export hash is the MD5 of payload JSON with sorted keys and compact separators (matches 53/53).
- calibration.json matches the Synapse CS2 profile on all 17 buttons, which confirms the Side/Tilt naming.
- Lighting:

| Lighting | Profiles |
|---|---|
| Static #00FF00 | APEXLEGENDS, CS2, DEADLOCK, paintgame, PEAK, raft, roblox, REPO, Schedule1, SPECTRE |
| Static #008000 | crabgame, rogue, splitgate, SeaOfthieves |
| Static #008080 | embr, Harrypotter, Valorant |
| Static #404040 | BF, Call of Duty, zomb |
| Static #FFFFFF | lethalcompany, Marathon, Tarky |
| Static #C06000 | Amongus |
| Static #808000 | Arc Raiders |
| Static #8080FF | fortnite |
| Static #FF8000 | lastofus |
| Static #804000 | PUBG |
| Static #FFC0C0 | rust |
| Static #C000C0 | SIEGE |
| Effect id 3 (inferred Spectrum; consistent with OpenRazer's spectrum effect id 0x03) | the other 19 profiles |

- Every profile has brightness 100.
- Seven empty side-button mappings: APEX Side3/Side4, deadrising Side1, Fallout76 Side2, Halo Side2, phas Side2/Side3.
- Tarky Side4 is mapped to middle click.
- Only Valorant (1) has no left/right click swap.
- Cfg.Save drops JSON fields the C# model does not know (Cfg.cs:64, 78), so new model fields must be added before new data.

### Protocol reference for LiteSynapse
- 91-byte report layout: [0] report ID 0, [1] status, [2] transaction ID (Naga LH 2020 0x1F, BlackWidow Lite 0x3F per OpenRazer), [3-4] remaining packets, [5] protocol type, [6] data size, [7] class, [8] command, [9-88] arguments, [89] CRC = XOR of bytes 3..88, [90] reserved.
- Mouse lighting, per OpenRazer razerchromacommon.c and the Naga LH 2020 cases in razermouse_driver.c:
  - Static: 0F/02, size 9, args {store, led, 0x01, 0, 0, 0x01, R, G, B}.
  - Spectrum: 0F/02, size 6, args {store, led, 0x03}.
  - None: 0F/02, args {store, led, 0x00}.
  - Brightness: 0F/04, size 3, args {store, led, value}.
  - LED zones: scroll 0x01, logo 0x04, right side 0x10.
  - Unverified: whether "store" should be the slot number for per-slot lighting, as LiteSynapse already does for brightness. Confirm with protocol.log.
- Button assignment 02/0C: type 2 = keyboard {mods, usage}; type 1 = mouse button (1 = left, 2 = right are verified via the click swap). Middle (3) and the "disabled" action are not verified.
- Waits: OpenRazer uses 600 µs between SET and GET. On busy, poll GET again and never re-send SET.

### Performance audit highlights
- Already done right: the hook is installed only when needed, there is no mouse hook, the hook path has no allocations, macros use pre-built batches and a high-resolution timer, and timeBeginPeriod is used only during macro playback.
- CS2 is the only game where the low-level keyboard hook is active.
- Snap Tap stuck-key bug: reproduced twice; the fix is verified (8/8 tests plus fuzz).
- Slot thrash is visible in log.txt lines 146-157.
- RazerDevice: fixed waits of 20/30/80 ms and a new handle opened per call.
- TrimMemory runs when the tray panel closes.
- No ReadyToRun build, non-concurrent GC, base thread priority 12, and no EcoQoS opt-out.
- Cfg.Log does file I/O on the input thread.
- Manually picking a profile sets inGame:true.

### Corrections from verification
1. DRDR.exe is also Steam 726910 "Star Wars: Droid Repair Bay", so the Dead Rising exes are not all unique.
2. SPECTRE: SpectreClient_BE.exe never owns the game window, SpectreClient.exe is only inferred, and the game was reportedly shut down in March 2025. Do not auto-link it.
3. The Steam DB has 24 "dealer" titles, not 25.
4. The jshost advice to make timeBeginPeriod on-demand is refuted: it already is (InputEngine.cs:718-742).
5. The driver hash suffixes were corrected (see Kernel drivers above).
6. Cfg.Log: only AppendAllText runs inside the lock.
7. CS2 automation: use a warning plus opt-in rather than silently turning it off, because the user chose these settings in Synapse.
8. Synapse's status polling applies only to default/suspend/exit command lists and to set-mode/LED effects. The web app may override the delays.
9. setLedState 03/00 is a matcher function, not an enum value.
10. The GMS3 per-GUID parse is also reached after a local/user list match. The benchmark ran on Linux.
11. RzCommon registers slots up to 13, but RzDev accepts only slots up to 10. The catalog lists about 714-737 hardware IDs.

### Not covered
- The "native" analysis delivered nothing: mapping_engine.dll, SysUtilsNative.dll, simple_service.dll, razer_elevation_service.exe and RazerAppEngine cpuz/cpuidsdk64.dll were not analysed.
- Nothing was run on Windows or real hardware; all latency figures are estimates.
- The Naga-specific command tables live in Razer's unreachable web app; a --logProtocol capture would close this gap.
- Exe names not found in either data source: Bungie Marathon, ARC Raiders and Valve Deadlock.


## PART 3: Razer native programs (re-run, verified: 38 confirmed, 2 refuted, 2 unverifiable)

mapping_engine.dll is the same file in 4.0.821, 4.0.823 and 4.0.827 (sha256 6eabdfde...320a, internal version 1.3.16.5). The 4.0.699 copy is 1.3.15.12 (sha256 e8ea6ddb...43fe). Both export the same 267 functions, have the same mapping_engine source-file list, and are built on Chromium base 116.0.5844.0. RazerAppEngine (the Electron app) loads the DLL in-process through ffi-napi from CommonDLL.

**Remapping.** Remapping does not use Windows hooks. It uses private IOCTLs to the Razer kernel filter stack (device type 0x8888; RzCommon.sys handles them). The engine:
- sets per-key hooks with SetInputHook,
- keeps an overlapped "read hooked input" IOCTL (0x88883018, 304-byte output) pending, completes it on an IO-completion-port thread and re-arms it in OnIOCompleted,
- injects the result back into the driver with a 32-byte "SendInput" IOCTL (0x88883020).

So every remapped key or button makes a kernel → user (RazerAppEngine) → kernel round trip. Macro mouse-moves and keyboard-executor repeats go out through user32 SendInput instead, one INPUT per call.

**Hooks.** Low-level hooks are used only in two places:
- The macro recorder installs WH_KEYBOARD_LL and WH_MOUSE_LL on "MappingEngineWindowHookThread" while recording.
- Global shortcuts install WH_KEYBOARD_LL and WH_MOUSE_LL on "MappingEngineGlobalShortcutThread" when enableGlobalShortcut is called. The mouse hook is skipped only when a debugger is attached. A WH_MOUSE_LL hook sends every mouse event, at full polling rate, through a RazerAppEngine thread, so this is a real input-latency risk.

Input monitoring uses Raw Input with RIDEV_INPUTSINK|RIDEV_DEVNOTIFY.

**Macro and turbo timing.** Delays are timed by a custom HighPrecisionWaitableEvent::TimedWait:
1. It raises the thread to THREAD_PRIORITY_HIGHEST.
2. For waits over 2 ms it arms a one-shot timeSetEvent(ms-2, resolution 0, TIME_CALLBACK_EVENT_SET). Resolution 0 asks for the finest system timer.
3. It then busy-spins on QueryPerformanceCounter plus WaitForSingleObject(…,0) for the last 2 ms or less. Waits of 2 ms or less are pure spin.

This puts a CPU-burning spin at high priority on every macro or turbo step.

Thread affinity is pinned only briefly, while macro data is parsed, and is restored afterwards. Engine threads are created at the default type, which maps to normal priority. There is no NtSetTimerResolution. The only ETW use is Chromium's TraceLogging provider. The DLL has no networking, services or autostart.

**HID.node** (node-rz-hid) is hidapi plus Razer extensions:
- sendFeatureReport goes to HidD_SetFeature, which is synchronous.
- getFeature and getInput use overlapped DeviceIoControl 0xB0192 and 0xB01A2.
- sendFeatureReportInBatch interprets a byte script: 0xF0 len8 data / 0xF1 ms (Sleep) / 0xF2 len32 data.
- setTimerResolution(ms) calls timeBeginPeriod(ms). It overwrites the stored value without calling timeEndPeriod on the old one, so repeated calls leak timer-resolution requests until clearTimerResolution or process exit.

**Other addons.** rz-usb-detect is event-driven (RegisterDeviceNotification on GUID_DEVINTERFACE_USB_DEVICE, with a Sleep retry of at most 5 s if enumeration fails). rzNotification is WinToast with WinHTTP and a Start-menu shortcut. ffi-napi-rz contains WinVerifyTrust plus "Razer USA Ltd." and DigiCert strings, so it likely checks the signer before loading a DLL. No Naga, 008D or 0235 references were found in mapping_engine.dll strings.

**Per binary**

- `mapping_engine.dll`: Synapse 4 software mapping engine: key/button remaps, macros, turbo, phased (advanced) macros, Hypershift, snaptap (SOCD), analog keys, global shortcuts, macro recorder, input monitoring, audio volume service, clipboard, keep-alive and idle detection, mouse sensor rotation. Version 1.3.16.5, Chromiu — performance: HIGH for macros and turbo. HighPrecisionWaitableEvent::TimedWait (VA 0x18011aa78, file high_precision_waitable_event_win.cc) is called from MappingExecutor::ExecuteTurboOnThread and ExecutePhasedMacroOnThread (VA 0x18012a805, 0x18012ab51, 0x18012ae3e, 0x18012aede, 0x18012afbc, 0x18012c2ee) and from many input-device paths. It works as follows:
- It saves the thread priority and calls SetThreadPrio — persists: No for the DLL itself. It is an in-process DLL inside RazerAppEngine. ADVAPI32 imports include only RegOpenKeyExW, RegQueryValueExW and RegCloseKey: no RegSetValue and no CreateService. There are no Run-key or task strings. Its LL hooks, Raw Input registrations and timers end with the process. Not v
- `mapping_engine.dll`: Older build (1.3.15.12, 4,333,256 bytes) of the same mapping engine, kept for comparison. — performance: Same timing and hook design as 827, so the same risks apply. 827 additionally coalesces injected mouse-move IOCTLs, which reduces the number of IOCTLs. — persists: No (in-process DLL, same as 827).
- `HID.node`: node-hid/hidapi (Windows) addon with Razer extensions. It is the transport for all HID feature-report traffic: lighting, settings and protocol commands. sha256 [hash], identical in synapse-x and synapse-big 827. The 699 build differs but has  — performance: MEDIUM if setTimerResolution is used. It raises the process timer resolution, which affects system timer behaviour and power use. Because the previous value is overwritten without timeEndPeriod, repeated calls leak timeBeginPeriod requests until process exit. clearTimerResolution releases only the last one.

LOW-MEDIUM: HidD_SetFeature is synchronous on whichever thread calls it. Large lighting ba — persists: No. It is a Node addon inside RazerAppEngine, and its timer-resolution requests end when the process exits.
- `detection.node`: USB plug/unplug detection for Synapse device discovery. — performance: Negligible. It is event-driven, with no polling while idle. — persists: No (in-process addon).
- `ffi_bindings.node`: Razer fork of ffi-napi. It is used to call the exported functions of mapping_engine.dll, SysUtilsNative.dll and simple_service.dll from JS. — performance: None observed. — persists: No.
- `binding.node (+ noble.node)`: WinRT Bluetooth LE (GATT) binding used for Razer BLE devices. — performance: LOW, and not relevant to USB-only devices: an active BLE scan can affect 2.4 GHz coexistence only while it runs. Not analysed in depth. — persists: No.
- `rzNotification.node`: Windows toast notifications (WinToast) for Razer Synapse 4, Chroma 4 and Virtual Ring Light. — performance: Negligible (on-demand only). — persists: Possibly a leftover Start-menu shortcut created by WinToast::createShortcut. This is inferred from the string; the shortcut path was not verified. There is no service or autostart.
- `detection.node`: Detection of BLE endpoint arrival and removal (companion to rz-usb-detect). — performance: Negligible. — persists: No.

**Recommendations**

- Do not load mapping_engine.dll or depend on its design. Do remaps on the device wherever the Naga or BlackWidow Lite supports onboard keymaps: that adds zero host latency and needs no user-mode round trip per press. Synapse instead routes each remapped input kernel → RazerAppEngine → kernel (IOCTL 0x88883018 read, then 0x88883020 inject).
- If LiteSynapse needs software remaps, avoid WH_MOUSE_LL entirely; Synapse installs one for global shortcuts, which puts every mouse report through a user thread. Use WH_KEYBOARD_LL only when strictly required, on a dedicated thread with an empty, non-blocking hook proc that only enqueues work. Prefer RegisterHotKey for shortcuts.
- Macro and turbo timing: do not copy Synapse's timeSetEvent(resolution 0) plus 2 ms QPC busy-spin at THREAD_PRIORITY_HIGHEST. Use CreateWaitableTimerExW with CREATE_WAITABLE_TIMER_HIGH_RESOLUTION (Windows 10 1803+), which gives about 0.5-1 ms accuracy without changing the global timer resolution or burning a core. Accept about 1 ms jitter or, at most, a very short spin (under 200 µs) that includes YieldProcessor/SwitchToThread. Run at ABOVE_NORMAL, not HIGHEST, and only while a macro is actually running.
- Never call timeBeginPeriod globally or persistently, unlike HID.node's setTimerResolution, which can leak requests. If a short macro needs it, pair each timeBeginPeriod with a timeEndPeriod around playback only.
- Batch SendInput: send all INPUTs for a step in one SendInput call. Synapse sends one INPUT per call (cbSize 0x28), which costs extra kernel transitions and lets events interleave.
- For lighting and settings, send feature reports with HidD_SetFeature on a single background worker thread at normal or below-normal priority. Serialise per device and keep a minimum gap of about 2 ms between commands, as the Synapse JS does. Do not send reports continuously during gameplay: push static or onboard effects once, then go idle.
- Use event-driven device detection like rz-usb-detect: RegisterDeviceNotification or CM_Register_Notification on GUID_DEVINTERFACE_USB_DEVICE (a5dcbf10-6530-11d2-901f-00c04fb951ed) or the HID interface GUID. Do not poll.
- Do not keep Raw Input INPUTSINK mouse monitoring on all the time; it means a WM_INPUT for every report at 1000 Hz or more. Use GetLastInputInfo on a slow timer (seconds) for idle detection, as Synapse's KeepAliveService does.
- To fully remove Synapse's input path for best latency, the RzDev_008d, RzDev_0235 and RzCommon filter drivers must also be uninstalled. Closing the app unloads mapping_engine's hooks, but the kernel filters stay in the input stack (see analysis/drivers.md and remove-razer-filter-drivers.ps1).
I covered every binary in the 'services' scope. Its core is three Windows Program Files folders: Razer\RazerAppEngine\app-4.0.827\CommonDLL (excluding mapping_engine.dll), Razer\razer_elevation_service and Razer\RazerAppEngine cpuz. The installer helpers are in Razer Synapse Setup, RazerAppEngine Setup and RazerAppEngine Upgrade Setup. I did not run any Razer binary. Facts come from import tables, strings and objdump/llvm disassembly. Five 827 binaries are byte-identical to the 821 or 699 builds already disassembled under analysis/native/dis (sha1 match), so those VAs apply unchanged.

**What stays installed or runs in the background:**
1. **Razer Elevation Service.** A LocalSystem COM service (razer_elevation_service.exe 1.1.0.8). It is demand-start and depends on RPCSS. It launches only programs signed by "Razer USA Ltd.", as SYSTEM or as the logged-on user. Its registration stays until someone removes it.
2. **HKCU Run autostart.** The value is "RazerAppEngine" = "<exe>" --url-params=… --autoStart=1. SysUtilsNative.setApplicationAutoStart writes it.
3. **A scheduled task named "Razer Upgrade Elevation Service".** RzPowerTool deletes it with schtasks during an elevation-service upgrade. I could not find what creates it.
4. **The CPUID SDK kernel driver (cpuz165).** cpuidsdk64.dll carries it inside the DLL and registers it at runtime as a demand-start kernel-driver service. The cpuz NSIS uninstaller never mentions this driver or service.
5. **Registry permission change.** The installer plug-in sfInstallHelper.dll gives Everyone GENERIC_ALL, inheritable, on HKLM\SOFTWARE\WOW6432Node\Razer.

**What runs only while Synapse is running:**
- RzEngineMon.exe is a watchdog started on every launch with --ppid. It wakes every 5 s, cleans up leftover Synapse processes and replays saved device-restore HID feature reports after a crash. It cannot relaunch Synapse because it has no CreateProcess or ShellExecute import.
- RzSecurityTool --verify-device-security runs on every launch. It rewrites the access rules (DACL) on Razer HID devices (vendor ID 0x1532) so that Everyone gets read/write.
- RzPowerTool and RzHandle are one-shot helpers.

**Most relevant to gaming performance (all in the RazerAppEngine.exe process):**
- **WMI process polling.** SysUtilsNative's CAppLaunchMonitor sets up WMI notification queries "SELECT * FROM __InstanceCreationEvent/__InstanceDeletionEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'". These make WMI poll the whole process list every second.
- **System-wide foreground hook.** SysUtilsNative installs an out-of-context WinEvent hook covering events 0x3–0x17 and runs a 300 ms timer.
- **1 ms timer resolution.** simple_service.dll contains Chromium's timer code, which calls timeBeginPeriod(1) or timeBeginPeriod(8). SysUtilsNative.SetProcessPowerThrottling(false) tells Windows to honour Synapse's timer-resolution requests even when it is hidden.

None of these binaries call SetWindowsHookEx (except the Chroma setup launcher's UI) or RegisterRawInputDevices. None set a process priority class or inject DLLs into other processes. None of them mention the Naga Left-Handed (008D) or the BlackWidow Lite (0235).

**Per binary**

- `823)`: Native helper DLL loaded into RazerAppEngine.exe main process via ffi (main.js registerMainDLL('sysUtilsNative')); 89 exports: autostart, foreground/keyboard-layout/session monitors, app-launch monitor, power throttling, Razer monitor list, Discord RPC, launchRzEngineMon, exit events, shutdown-block — performance: MEDIUM. The WMI 'WITHIN 1' queries make WMI (WmiPrvSE) re-enumerate Win32_Process every second for each of the two queries while app-launch registration is active. This is a known source of periodic CPU blips and a plausible micro-stutter source. The out-of-context WinEvent hook loads nothing into games, but every foreground, minimize or move-size event in the system wakes RazerAppEngine; there is — persists: The DLL is in-process only and stops when RazerAppEngine.exe exits. The HKCU Run value 'RazerAppEngine' it writes persists and relaunches Synapse at logon unless removed.
- `simple_service.dll (v1.2.2.10) and simple_service_v1.2.2.9.dll (v1.2.2.9, same size, staged copy)`: A DLL built on Chromium's //base library and loaded into RazerAppEngine.exe (FFISimpleService). Exports: simpleLaunchRazerApp/…Elevated/…NoWait, simpleLaunchUserApp*, audio endpoint control (speaker/mic/sidetone volume, device-change events), user-app file management, simpleGetHidDeviceProperties. I — performance: LOW to MEDIUM. Chromium's code can hold system timer resolution at 1 ms while RazerAppEngine has short delayed tasks pending. Together with SetProcessPowerThrottling(false) this stays in effect while the window is hidden. That raises the timer-interrupt rate and power use system-wide; it does not directly reduce FPS. No hooks, no injection. — persists: No. In-process only.
- `RzEngineMon.exe (v1.0.7.0, sha1 29d23daf…, identical 699–827)`: Crash and exit watchdog for RazerAppEngine plus a device-restore tool. It statically links hidapi (exports hid_*). — performance: Negligible. It sleeps in 5 s waits and has no hooks or timers that touch input. — persists: No. It exits once RazerAppEngine has exited and cleanup is done. It is not registered for autostart and is started only by RazerAppEngine. The exact final exit path was not fully traced.
- `RzHandle.exe (v1.0.3.0)`: One-shot command-line tool, 'RzHandle.exe -p COMx'. It finds which process holds a serial (COM) port handle. — performance: Low and only on demand. The system-wide handle walk is a short CPU burst. It does not stay running. — persists: No.
- `RzPowerTool.exe (sha1 9d7a2a2b…)`: One-shot privileged maintenance tool, run through the elevation service (simpleLaunchRazerApp) or as the user (simpleLaunchUserAppProcess from User Data\Apps\Common). — performance: None at runtime. It is short-lived. Stopping 'Audiosrv' through it would interrupt audio, but that only happens on an explicit request. — persists: No.
- `RzSecurityTool.exe (v1.0.4.0, sha1 323892ed…)`: Fixes the access rules on Razer HID devices so that Synapse can open them. — performance: None. One-shot at startup. — persists: The binary does not persist. The DACL it sets is on the device object; I infer, but did not verify, that it lasts until the device re-enumerates (replug or reboot).
- `razer_elevation_service.exe (v1.1.0.8)`: 'Razer Elevation Service': an out-of-process COM server running as a Windows service (ATL plus Chromium //base). It lets the non-elevated RazerAppEngine run Razer-signed binaries as SYSTEM, or as the logged-on user, and copy Razer files. — performance: Low. It is idle unless called over COM. Security note: it is a SYSTEM-level launcher that accepts arbitrary parameters for any Razer-signed binary (for example RzPowerTool --stop-service). — persists: YES, unless removed. The service registration and COM keys stay until deleted. Start type is demand: COM activation starts it, not boot. RzPowerTool's allowlist uses the service key name 'razer_elevation_service', while the ATL self-install would name it 'Razer Elevation Service'; check for both. A 
- `cpuidsdk64.dll (+ Uninstall.exe NSIS)`: CPUID SDK (the CPU-Z/HWMonitor engine) for hardware and sensor information. It carries kernel drivers inside the DLL. — performance: Potentially HIGH while the SDK is polling sensors, but not measured. The driver gives ring-0 access to MSR, PCI and SMBus hardware, a class of tool known to cause DPC latency spikes when sensors are polled. The loaded kernel driver also stays resident until stopped. — persists: POSSIBLY. The cpuz165 kernel-driver service is created at runtime and the NSIS uninstaller has no code to remove it. I did not trace whether the SDK deletes it on unload. The RunOnce values may also remain if an uninstall was interrupted.
- `sfInstallHelper.dll (identical, v1.0.5.0, x86)`: Setup Factory installer plug-in: registry ACL grant, launching apps as the user, AppUserModelID on shortcuts, signature checks, WOW64 file-system redirection helpers. — performance: None at runtime. — persists: The ACL change persists: Everyone keeps full control on HKLM\SOFTWARE\WOW6432Node\Razer and its subkeys for as long as the key exists.
- `Uninstall-Synapse.exe (NSIS 3 Unicode uninstaller stub)`: A wrapper that runs the real Setup Factory uninstaller. — performance: None. — persists: The Setup Factory uninstaller data under C:\Windows\Installer\Razer\Razer Synapse\ remains until the uninstall finishes.
- `RazerChroma-Web-Internal-bundled-v.exe`: Setup Factory launcher (OriginalFilename suf_launch.exe), 'Razer Chroma 4.0.827', FileVersion 4.0.827.0. — performance: None after installation, as far as the launcher itself goes. The payload contents are unknown. — persists: Unknown: the payload was not decoded.
- `Apps-Common.zip`: Icon assets only. — performance: None. — persists: Not applicable.

**Recommendations**

- For LiteSynapse game or app detection, do not use WMI '__InstanceCreationEvent WITHIN n' polling as SysUtilsNative does. Prefer SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, NULL, cb, 0, 0, WINEVENT_OUTOFCONTEXT|WINEVENT_SKIPOWNPROCESS), limited to the single event rather than Razer's 0x3–0x17 range, with no polling timer.
- Do not call timeBeginPeriod. Do the opposite of Razer's SetProcessPowerThrottling(false): when idle or hidden, call SetProcessInformation(ProcessPowerThrottling) with ControlMask = EXECUTION_SPEED|IGNORE_TIMER_RESOLUTION and StateMask set (EcoQoS), so LiteSynapse never raises the system timer rate during games.
- LiteSynapse needs no SYSTEM elevation service. RzSecurityTool's DACL rewrite shows Synapse depends on Everyone having read/write on VID 0x1532 HID interfaces. Open vendor collections with desired access 0 or GENERIC_READ|GENERIC_WRITE and FILE_SHARE_READ|FILE_SHARE_WRITE. If ERROR_ACCESS_DENIED happens, report it rather than installing a privileged helper.
- If LiteSynapse puts a device into a software or driver mode, copy Razer's crash-safety idea without a watchdog process. Keep settings on the device where possible, or restore the device in a finally/unhandled-exception handler and at session end. Use 91-byte feature buffers for protocol 2.5 commands (report ID plus 90 bytes).
- Autostart: use one HKCU Run value or a per-user logon scheduled task without highest privileges, and have LiteSynapse's uninstaller remove it. Delete Razer's own HKCU Run value 'RazerAppEngine' so the two apps do not both start.
- Removal script, run elevated after uninstalling Synapse (Razer service names are matched case-insensitively). (1) taskkill /F /IM RazerAppEngine.exe /IM RzEngineMon.exe /T. (2) reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v RazerAppEngine /f. (3) schtasks /Delete /TN "Razer Upgrade Elevation Service" /F. (4) sc.exe stop razer_elevation_service and sc.exe delete razer_elevation_service; also check sc.exe query "Razer Elevation Service" and delete it if present. (5) Delete the COM keys: HKLM\SOFTWARE\Classes\CLSID\{0EDEAF3C-D36E-4E7E-9467-900B977DC4FF}, HKLM\SOFTWARE\Classes\AppID\{9C877A5A-7FE0-4E2B-9735-60420DE27084}, HKLM\SOFTWARE\Classes\AppID\RazerElevationService.EXE, HKLM\SOFTWARE\Classes\RzUtility.Elevator, HKLM\SOFTWARE\Classes\RzUtility.Elevator.1, HKLM\SOFTWARE\Classes\TypeLib\{F24A9E29-FC07-4277-9BBE-8315A7735BF0}. (6) rd /s /q "C:\Program Files\Razer\razer_elevation_service". (7) Run sc.exe query type= driver state= all | findstr /i cpuz; if cpuz165 exists, sc.exe stop cpuz165 and sc.exe delete cpuz165. (8) reg delete "HKLM\Software\Microsoft\Windows\CurrentVersion\RunOnce" /v "RazerAppEngine cpuz-installer" /f, and the same for /v "RazerAppEngine cpuz-uninstaller". (9) Delete HKLM\SOFTWARE\Razer\Synapse4\RazerAppEngine cpuz and HKLM\SOFTWARE\WOW6432Node\Razer\Synapse4\RazerAppEngine cpuz. (10) rd /s /q "C:\Program Files\Razer\RazerAppEngine cpuz" and "C:\Program Files\Razer\RazerAppEngine cpuz Temp".
- Removal, continued. (11) For each of 'Razer Game Manager Service 3', 'Razer Chroma SDK Server', 'Razer Chroma SDK Service', 'Razer Chroma Stream Server', 'Razer Synapse Service', 'RazerExperienceService', 'HapticService' and 'Carol Routing Service', run sc.exe query; if it exists and its binary is under a Razer folder, stop it and set it to disabled or delete it. Never touch Audiosrv, VSSrv or XTU3SERVICE, which appear on RzPowerTool's allowlist but are not Razer services. (12) Delete HKLM\SOFTWARE\WOW6432Node\Razer, or at least remove its 'Everyone: Full control' entry, which sfInstallHelper added. (13) Delete HKCU\SOFTWARE\Razer\RazerAppEngine_product and HKCU\SOFTWARE\Razer\RzAppEngine_product. (14) Delete %LOCALAPPDATA%\Razer\RazerAppEngine, which holds User Data (Logs, Devices\Restore, Crashpad, Apps). (15) Delete C:\Windows\Installer\Razer\Razer Synapse and ...\Installer\Razer\RazerAppEngine cpuz Log; the prefix of the cpuz path is an unresolved NSIS variable, likely %WINDIR%.
- Do not reuse or ship cpuidsdk64.dll or any CPUID SDK component. Loading its cpuz165 kernel driver and polling sensors is unnecessary for mouse and keyboard control, and drivers of that kind are known to risk DPC latency.

**Verifier corrections**

- refuted: [mapping_engine protocol] RzCommon.sys dispatches the same IOCTL codes. — Only partly true. analysis/drivers/rzcommon.asm dispatches 0x88883004-0x88883048 (including 0x18/1C/20/24/2C/30/34/38), plus 0x88883080-0x88883144. It has no 0x88883180 or 0x88883184 (SetMouseRotation/GetMouseRotation). rzdev.asm contains only 0x88883010, 0x88883014 and 0x88883140. The handler for the rotation IOCTLs is unknown, and they may go to a different device handle.
- unverifiable: [rzNotification persistsAfterUninstall] It possibly leaves a Start-menu shortcut created by WinToast::createShortcut. — Stronger evidence than the finding gave: the strings '\Microsoft\Windows\Start Menu\Programs\Razer\' (0xb0c20) and '.lnk' (0xb0c80) are present, together with the SHGetSpecialFolderPathW import, so the shortcut location is %APPDATA%\…\Programs\Razer\*.lnk. Whether the uninstaller removes it cannot be determined because uninstall.xml is absent.
- refuted: [simple_service.dll perfRisk] Chromium timer code can hold the system timer at 1 ms: timeBeginPeriod(1) when high-res is enabled, otherwise 8. With SetProcessPowerThrottling(false), the 1 ms requests  — The code shape matches (0x180076939-0x180076955: 1 if [0x18025efe8] else 8; 0 if the count [0x18025efec] is 0; timeEndPeriod at 0x180076a39, timeBeginPeriod at 0x180076a6d). However, the enable flag at 0x18025efe8 is in zero-initialised .data and has no writer anywhere in the disassembly; only the read at 0x18007693f references it. So this DLL can only request timeBeginPeriod(8).
The same holds in
- unverifiable: [cpuidsdk64 perfRisk] Potentially HIGH DPC latency while polling sensors. — No consumer of cpuidsdk64.dll was found in JS or the binaries, and no polling interval was identified. The risk is generic to MSR/SMBus drivers and was not shown for this install.
