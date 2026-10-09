# LiteSynapse 1.1 test build: what to try

This is the first test build. Everything was built and tested in the cloud (358 automated tests), but this is the first time it runs on Windows with your Naga and BlackWidow. Please go through this list and tell me what works and what doesn't.

## Install

1. Close the old LiteSynapse: tray icon → **Exit**. (The installer also closes it.)
2. Recommended: quit Razer Synapse fully (tray icon → **Quit**) so it can't fight over the mouse.
3. Run `LiteSynapse-Setup-1.1.0.exe`. It installs just for you, so there's no admin prompt.
   - Windows SmartScreen will say "Windows protected your PC", because the app isn't code-signed. Click **More info → Run anyway**.
   - If it asks for the .NET 10 Desktop Runtime or WebView2, click Yes. That opens Microsoft's download page.
4. Leave "Start LiteSynapse with Windows" the way you want it, then click **Finish** with "Launch" ticked.

Your profiles, games and macros are copied from `Documents\LiteSynapse\data`. The old folder isn't touched.

## Checklist

| # | Try this | Expected |
|---|---|---|
| 1 | Open LiteSynapse from the Start menu | New dark window, no white flash. 47 mouse profiles, your keyboard profile, 2 macros, 47 games |
| 2 | **Settings → Maintenance → Run self-test** | A console window runs checks. Send me a screenshot of the end |
| 3 | **Mouse → Performance**: change DPI, then set it back | The mouse speed changes right away |
| 4 | Start CS2 | Within about a second the mouse switches to your CS2 profile; Overview shows "In game" |
| 5 | In CS2, alt-tab out and back quickly (under 3 s) | The mouse does **not** switch slots back and forth |
| 6 | Leave CS2 for more than 3 seconds | The mouse goes back to slot 3 (Green) |
| 7 | **Offline/practice server only:** hold A, press D, release D | Snap Tap: you move right, then left again |
| 8 | **Offline/practice server only:** hold A, alt-tab out, release A, alt-tab back | Nothing stays stuck |
| 9 | **Offline/practice server only:** press the arrow keys bound to Jumpthrow / W Jumpthrow | The macros fire |
| 10 | **Keyboard → Lighting**: try Static, Breathing, Reactive, Starlight, Off and the brightness slider | The keyboard backlight follows |
| 11 | **Mouse → Lighting** (experimental): pick a static color | Tell me which zones changed (scroll wheel, logo, side), or if nothing did |
| 12 | **Mouse → Onboard memory**: write a profile to slot 4, then press the mouse's Cycle Profile button to slot 4 | The side buttons send that profile's keys |
| 13 | **Settings → Performance** | Shows what Razer leftovers are on your PC. Don't run the driver removal yet; just tell me what it shows |
| 14 | Close the LiteSynapse window, then open Task Manager | No `msedgewebview2.exe` processes left from LiteSynapse |
| 15 | **Games**: start a game that isn't linked, switch to LiteSynapse, click "Link the game that's running" | The game gets added to that profile |

**Don't test Snap Tap or the macros in Valve matchmaking or FACEIT.** Valve kicks for Snap Tap-style input and one-key multi-action binds on its official servers. Use a practice or offline server.

## If something goes wrong

- **Settings → Open config folder** opens `%LOCALAPPDATA%\Programs\LiteSynapse\data`. Send me `log.txt` from there.
- If the new window doesn't open, LiteSynapse falls back to the old window automatically. Tell me if that happens.
- To go back: uninstall LiteSynapse (Settings → Apps; it keeps your profiles), then run the old `Documents\LiteSynapse\LiteSynapse.exe`.
