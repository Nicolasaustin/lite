LiteSynapse - lightweight Razer Synapse replacement for the Naga Left-Handed Edition + BlackWidow Lite

Tray icon
  Left click   small panel: pick the mouse and keyboard profile, see what the mouse is running, turn automatic switching on/off,
               open the full window.
  Right click  LiteSynapse (open the window) / Settings / Start with Windows / Exit.
  Picking a profile in the panel applies it fully and keeps it (automatic switching turns off) until you switch it back on.

Starting profile
  CS2 (mouse) and "gamer - S3" (keyboard) are used when LiteSynapse starts and whenever no game is running.
  Change it: Naga or BlackWidow tab -> "..." menu -> "Set as starting profile".
  When a game from the Linked Games list is in front, its profile is written to the mouse and its macros / Snap Tap are active.
  When you leave the game, the mouse goes back to the slot you were using.

Files
  Everything lives in the "data" folder next to LiteSynapse.exe: profiles.json, games.json, settings.json, calibration.json,
  tabs.json, onboard.json, log.txt. Settings -> "Open config folder" opens it.

On-Board Memory (Naga page, memory icon next to the "..." menu):
  pick a profile for slot 2-5 and it is written to the mouse. Cycle Up Profile on the mouse switches slots.
  1 white (live), 2 red, 3 green, 4 blue, 5 cyan. Selecting a slot resets it first, then writes the profile.

Checks (run from a console)
  LiteSynapse.exe test                    reads the devices, swallows its own test keystrokes
  LiteSynapse.exe uitest [picture.png]    drives the tray panel and menu on a hidden desktop (nothing appears on screen)
  LiteSynapse.exe writeslot <2-5> <profile name>   write a profile to a slot and read it back
  LiteSynapse.exe import <file.synapse4> [more files / macro .xml]   re-import a Synapse export
