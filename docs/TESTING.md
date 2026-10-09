# LiteSynapse 1.1.2 test build 3: what to try

Test build 3 adds what you asked for after build 2. Buttons work the way Synapse does it (factory default unless
you remap, no button calibration). There's an Exit button in the window, and the app uses your new round logo. It
also includes everything from build 2.

## Install

1. Run `LiteSynapse-Setup-1.1.2.exe`. It closes the running LiteSynapse, replaces the old build and keeps your
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
