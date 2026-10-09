# LiteSynapse 1.1.1 test build 2: what to try

Test build 2 fixes what you found in build 1: remaps, DPI, polling and lighting now go to the mouse right away, the
profile you click stays on the mouse until you exit LiteSynapse, keyboard macros work with a profile you picked, and the
Buttons page now looks like Synapse's Customize page.

## Install

1. Run `LiteSynapse-Setup-1.1.1.exe`. It closes the running LiteSynapse, replaces the old build and keeps your
   profiles, games and macros. No uninstall needed.
   - SmartScreen says "Windows protected your PC" because the app isn't code-signed: **More info → Run anyway**.
2. Quit Razer Synapse fully (tray icon → **Quit**) so it can't fight over the mouse and keyboard.
3. Click **Finish** with "Launch" ticked.

## What changed

- **The mouse runs the profile LiteSynapse shows, all the time.** On the desktop that's your starting profile, in a
  linked game the game's profile, and when you click a profile, that one. Your own onboard slot (3, green) comes
  back only when you exit LiteSynapse (tray icon → Exit).
- **Changes apply right away**: button remaps, DPI, polling rate and lighting. DPI and polling within a moment,
  remaps and lighting about a second after your last change.
- **A profile you click stays.** Clicking a profile turns auto-switch off. Its keyboard macros and Snap Tap then
  work everywhere, except inside the LiteSynapse window. Turn auto-switch back on to follow your games again.
- **Keyboard lighting**: Off, Static and Breathing only. Those are the only effects the BlackWidow Lite has, which
  is why Reactive and Starlight did nothing. If you had picked one of them, it's now Static.
- **Mouse → Buttons** now has a Synapse-style top view (lines to each button) and side view (the 12 numbered
  buttons). Switch with the two thumbnails under the mouse. Click any key name to change it.

## Checklist

| # | Try this | Expected |
|---|---|---|
| 1 | Open LiteSynapse | Overview shows your starting profile running on the mouse ("… is running on the mouse (slot 1 or 2)") |
| 2 | **Mouse → Buttons**: click a key name, then pick a key | The label turns green. About a second later the button sends the new key (try it in Notepad) |
| 3 | **Mouse → Buttons**: switch to the side view and change side button 1 | Same as above |
| 4 | **Mouse → Performance**: change DPI | The mouse speed changes right away |
| 5 | **Mouse → Performance**: change polling rate | Saved and applied, no error |
| 6 | **Mouse → Lighting**: pick a static color | Scroll wheel, logo and side panel change color. Tell me which zones change |
| 7 | **Keyboard → Lighting**: Off, Static, Breathing and the brightness slider | The backlight follows |
| 8 | Click a keyboard profile in the profile switcher, then open Notepad and press an arrow key that has a macro | The macro runs (in Notepad, not in the LiteSynapse window) |
| 9 | Turn auto-switch back on, start CS2 | Within about a second the mouse switches to your CS2 profile |
| 10 | Alt-tab out of CS2 and back quickly (under 3 s) | The mouse doesn't switch back and forth |
| 11 | Tray icon → **Exit** | The mouse goes back to its own slot 3 (green) |

**Don't test Snap Tap or macros in Valve matchmaking or FACEIT.** Valve kicks for Snap Tap-style input and one-key
multi-action binds on its official servers. Use a practice or offline server.

## If something goes wrong

**Settings → Open config folder** opens `%LOCALAPPDATA%\Programs\LiteSynapse\data`. Send me `log.txt` from there.
It shows each write to the mouse ("live slot …", "mouse lighting …", "mouse now: dpi …").
