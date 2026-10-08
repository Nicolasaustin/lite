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
