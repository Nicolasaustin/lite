# LiteSynapse window ↔ app bridge (v1.2)

The new main window is a single HTML page (`src/LiteSynapse/ui/index.html`) hosted in WebView2.
It talks to the C# app only through this message bridge. Both sides MUST follow this file exactly.
All JSON property names are camelCase.

## Transport

- JS → C#: `window.chrome.webview.postMessage({ id, cmd, args })`
  - `id`: integer, unique per call. `args`: object (may be `{}`).
- C# → JS reply (exactly one per request):
  - success: `{ "type": "reply", "id": <id>, "ok": true, "result": <any> }`
  - failure: `{ "type": "reply", "id": <id>, "ok": false, "error": "<human readable>" }`
- C# → JS pushes (any time):
  - `{ "type": "state", "state": <State> }` — after every mutating command and whenever `Service.Changed` fires (debounced ~100 ms).
  - `{ "type": "progress", "op": "onboard", "text": "Writing buttons..." }` — during long operations.
- Replies are sent with `CoreWebView2.PostWebMessageAsJson`. JS listens with `window.chrome.webview.addEventListener('message', e => e.data)`.
- When the page is opened outside WebView2 (no `window.chrome.webview`), it uses a mock bridge (`tools/ui-preview/mock-bridge.js`) for previews only.

## State object

```jsonc
{
  "version": "1.1.0",
  "mouse":    { "connected": true, "name": "Naga Left-Handed Edition" },
  "keyboard": { "connected": true, "name": "BlackWidow Lite" },
  "auto": true,              // automatic game switching
  "inGame": false,           // a linked game is in front
  "liveWrite": true,         // "run selected profiles on the mouse itself"
  "startup": false,          // start with Windows
  "liveStatus": "\"CS2\" is running on the mouse (slot 1, White)",
  "activeMouse": "CS2",
  "activeKeyboard": "gamer - S3",
  "defaultMouse": "CS2",       // starting profile
  "defaultKeyboard": "gamer - S3",
  "mouseProfiles": [
    { "name": "CS2", "pollingRate": 1000, "dpi": 800, "dpiStages": [400, 800], "dpiActive": 2,
      "buttons": { "Side8": { "key": "KEY_W", "mods": [] }, "Side3": { "key": "DISABLED", "mods": [] } },
      "lighting": { "effect": "static", "color": "#00FF00", "brightness": 100 },   // per profile; null = leave mouse as is
      "clickSwap": true,          // left-handed click swap written to onboard memory
      "antiCheat": true }         // game is flagged as anti-cheat protected
  ],
  "keyboardProfiles": [
    { "name": "gamer - S3", "brightness": 7, "snapTap": true,
      "macroKeys": { "KEY_UP_ARROW": "W Jumpthrow" },
      "effect": "static", "effectSpeed": 2 }
  ],
  "macros": [ { "name": "Jumpthrow", "steps": [ { "vk": 109, "ext": false, "down": true, "delayMs": 0 } ] } ],
  "games": [ { "profile": "CS2", "exes": ["cs2.exe"], "keyboardProfile": "gamer - S3" } ],
  "calibration": { "Side1": "3", "Scroll": "Space" },   // label of the key each button sends; missing id = not calibrated
  "buttonIds": ["Side1", "...", "Side12", "Scroll", "M1", "M2", "TiltL", "TiltR"],
  "onboard": { "3": "CS2" },        // slot number (string) -> profile name last written there
  "liveSlots": [1, 2],              // slots LiteSynapse uses for live profiles ([] when liveWrite is off)
  "accent": "mint",                 // UI accent: mint | green | blue | violet | amber
  "competitiveSafe": false,         // when on: no Snap Tap, macros or software remaps in anti-cheat games
  "gameKeyboardDefault": "",        // keyboard profile used for games whose rule has none ("" = none)
  "lastGameExe": "cs2.exe",         // last foreground program that is not LiteSynapse/Explorer (for "link running game")
  "elevatedGame": false,            // the foreground game runs as admin and LiteSynapse does not (Snap Tap/macros can't reach it)
  "razer": {                        // leftovers of Razer software that cost performance
    "checked": true, "synapseInstalled": false,
    "gmsService": "absent",         // absent | running | stopped | disabled
    "filterDrivers": ["RzDev_008d (Naga)", "RzDev_0235 (BlackWidow Lite)"], "rzCommon": false
  }
}
```

Button targets: any `KEY_*` name, or `DISABLED` (button does nothing) or `MOUSE_MIDDLE` (middle click).
Key names use the existing `KEY_*` form (`KEY_W`, `KEY_SPACEBAR`, `KEY_LEFT_SHIFT`, `KEY_F13`, ...) — see `Keys2.cs` / `OnboardMemory.Usage`.
Macro steps use Windows virtual-key codes (`vk`), `ext` = extended-key flag, `vk: 0` = pure delay step.

## Commands

| cmd | args | result |
|---|---|---|
| `getState` | `{}` | State |
| `getDeviceInfo` | `{}` | `{ "mouse": { "dpi": 800, "polling": 1000, "slot": 3, "slotName": "Green" } \| null, "keyboard": { "brightness": 7 } \| null }` (HID reads, may take ~1 s) |
| `selectProfile` | `{ kind: "mouse"\|"keyboard", name }` | State (turns automatic switching off, like the tray panel) |
| `setAuto` | `{ on }` | State |
| `setLiveWrite` | `{ on }` | State |
| `setStartup` | `{ on }` | State |
| `setDpiStages` | `{ stages: int[1..5] (100..20000), active: int (1-based) }` | State — active mouse profile; `dpi = stages[active-1]`; applied to the mouse |
| `setPolling` | `{ hz: 125\|500\|1000 }` | State |
| `setButton` | `{ id, key, mods: string[] }` | State — `key: ""` removes the remap (button sends its default) |
| `setKbBrightness` | `{ value: 0..100 }` | State |
| `setKbEffect` | `{ effect: "off"\|"static"\|"breathing"\|"reactive"\|"starlight", speed: 1..3 }` | State |
| `setSnapTap` | `{ on }` | State |
| `setMacroKey` | `{ key: "KEY_UP_ARROW"\|"KEY_DOWN_ARROW"\|"KEY_LEFT_ARROW"\|"KEY_RIGHT_ARROW", macro: name\|"" }` | State |
| `addProfile` | `{ kind, name }` | State |
| `renameProfile` | `{ kind, from, to }` | State |
| `duplicateProfile` | `{ kind, name }` | `{ name: "<new name>" }` |
| `deleteProfile` | `{ kind, name }` | State (error if it is the starting profile) |
| `resetProfile` | `{ kind, name }` | State (mouse: clears button remaps; keyboard: clears macro keys) |
| `setStartingProfile` | `{ kind, name }` | State |
| `exportProfile` | `{ kind, name }` | `{ path: string\|null }` (native Save dialog) |
| `importProfiles` | `{}` | `{ message: string }` (native Open dialog: .synapse4 / .xml) |
| `addGame` | `{ profile }` | State |
| `setGameExes` | `{ profile, exes: string[] }` | State |
| `setGameKeyboard` | `{ profile, keyboardProfile }` (`""` = none) | State |
| `removeGame` | `{ profile }` | State |
| `runningApps` | `{}` | `string[]` of `name.exe` for processes with a visible window, sorted, deduped |
| `saveMacro` | `{ originalName: string\|null, macro: { name, steps } }` | State (rename updates keyboard macro-key references) |
| `deleteMacro` | `{ name }` | State (removes references from keyboard profiles) |
| `onboardWrite` | `{ slot: 2..5, profile }` | `{ ok: bool, message }` (sends `progress` pushes while writing) |
| `calibrate` | `{}` | `null` — opens the native calibration window |
| `probeMouseLighting` | `{}` | `{ zones: [ { id: 0, name: "All zones" }, ... ] }` (read-only probe) |
| `setMouseLighting` | `{ profile?: name (default: active), effect: "off"\|"static"\|"breathing"\|"spectrum", color: "#RRGGBB", brightness: 0..100 }` | State — saved on that mouse profile, applied now if it is active |
| `setCompetitiveSafe` | `{ on }` | State |
| `setGameKeyboardDefault` | `{ keyboardProfile }` (`""` = none) | State |
| `linkRunningGame` | `{ profile }` | `{ exe: string\|null }` — adds `state.lastGameExe` to that profile's game rule (creates the rule if missing) |
| `razerCheck` | `{}` | State (refreshes `state.razer`) |
| `razerCleanup` | `{ action: "disableGameManager"\|"driverGuide" }` | `{ message }` — runs the elevated step with a UAC prompt |
| `runSelfTest` | `{}` | `null` — opens a console window running `LiteSynapse.exe testall` |
| `openConfigFolder` | `{}` | `null` |
| `reloadConfig` | `{}` | State |
| `openMouseProperties` | `{}` | `null` |
| `openClassicWindow` | `{}` | `null` — opens the old WinForms window |
| `getLog` | `{ lines: 200 }` | `string` |
| `setAccent` | `{ accent }` | State |

Unknown commands reply `ok: false`.

## C# APIs the bridge calls

Owned by the data/protocol work:
```csharp
public class MouseLightingSettings { string Effect = "static"; string Color = "#00FF00"; int Brightness = 100; }
// MouseProfile gains: MouseLightingSettings Lighting (null = leave as is); bool ClickSwap = true
// KeyTarget.Key may be "DISABLED" or "MOUSE_MIDDLE"
internal static class AntiCheat { public static bool IsProtected(string profileName); public static readonly string[] Profiles; }
internal static class DataMigrations { public static bool Apply(ProfileStore store, List<GameRule> games); } // add-only, idempotent
```
Owned by the device/service work (Service.cs):
```csharp
public bool CompetitiveSafe { get; }            public void SetCompetitiveSafe(bool on);
public string GameKeyboardDefault { get; }      public void SetGameKeyboardDefault(string name);
public string LastGameExe { get; }              public string LinkRunningGame(string profile); // returns exe or null
public bool ElevatedGame { get; }
public RazerLeftovers Razer { get; }            public void RefreshRazer(); public string RazerCleanup(string action);
public string ApplyMouseLighting(MouseProfile p);   // null = ok; no-op if the mouse is disconnected
public bool ApplyKbLighting(KbProfile p);
public void OnSuspend(); public void OnResume(); public void OnSessionLock(); public void OnSessionUnlock();
public class RazerLeftovers { bool Checked; bool SynapseInstalled; string GmsService; List<string> FilterDrivers; bool RzCommon; }
```

## Older helper signatures (still valid)

```csharp
internal static class KbLighting {
    public static readonly string[] Effects;            // "off","static","breathing","reactive","starlight"
    public static bool Apply(RazerDevice kb, string effect, int speed, int brightness); // brightness 0..100
}
public class MouseLightingState { string Effect; string Color; int Brightness; List<int> Zones; }
internal static class MouseLighting {
    public static MouseLightingState Load();
    public static void Save(MouseLightingState s);
    public static List<(int Id, string Name)> Probe(RazerDevice mouse);
    public static string Apply(RazerDevice mouse, MouseLightingState s);   // null = ok, else error text
}
// KbProfile gains: string Effect = "static"; int EffectSpeed = 2;
```
