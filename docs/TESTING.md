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
