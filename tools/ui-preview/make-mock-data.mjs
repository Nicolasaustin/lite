// Generates tools/ui-preview/mock-data.json (camelCase, docs/bridge-api.md v1.2 "State" shape)
// from the user's real config in LiteSynapse/data (PascalCase, as saved by Cfg.cs).
//
//   node tools/ui-preview/make-mock-data.mjs [dataDir]
//
// The app's own tables are read from the C# sources so the preview shows what the app will show:
//   AntiCheat.cs       -> mouseProfiles[].antiCheat
//   DataMigrations.cs  -> per-profile lighting, disabled / middle-click side buttons, extra game exes
// Preview only: nothing here ships with the app.
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const repo = resolve(here, '..', '..');
const dataDir = resolve(process.argv[2] || join(repo, 'LiteSynapse', 'data'));
const srcDir = join(repo, 'src', 'LiteSynapse', 'LiteSynapse');

const load = (name, fallback) => {
  const p = join(dataDir, name);
  if (!existsSync(p)) return fallback;
  const txt = readFileSync(p, 'utf8').replace(/^﻿/, '');
  return name.endsWith('.json') ? JSON.parse(txt) : txt;
};
const source = name => {
  const p = join(srcDir, name);
  return existsSync(p) ? readFileSync(p, 'utf8') : '';
};
// Body of the first `{ ... }` initializer that follows `marker` in a C# file.
const block = (text, marker) => {
  const at = text.indexOf(marker);
  if (at < 0) return '';
  const open = text.indexOf('{', at);
  let depth = 0;
  for (let i = open; i < text.length; i++) {
    if (text[i] === '{') depth++;
    else if (text[i] === '}' && --depth === 0) return text.slice(open + 1, i);
  }
  return '';
};
const strings = text => [...text.matchAll(/"((?:[^"\\]|\\.)*)"/g)].map(m => m[1]);

// ---- App tables (C# sources) ---------------------------------------------------------------------
const antiCheatSrc = source('AntiCheat.cs');
const antiCheat = new Set(strings(block(antiCheatSrc, 'Profiles =')).map(s => s.toLowerCase()));

const migSrc = source('DataMigrations.cs');
const staticColors = Object.fromEntries(
  [...block(migSrc, 'StaticColors =').matchAll(/\["([^"]+)"\]\s*=\s*"(#[0-9A-Fa-f]{6})"/g)].map(m => [m[1], m[2].toUpperCase()]));
const spectrum = new Set(strings(block(migSrc, 'SpectrumProfiles =')));
const buttonFixes = [...block(migSrc, 'ButtonFixes =').matchAll(/\("([^"]+)",\s*"([^"]+)",\s*KeyTarget\.(\w+)\)/g)]
  .map(m => ({ profile: m[1], button: m[2], key: m[3] === 'Disabled' ? 'DISABLED' : 'MOUSE_MIDDLE' }));
const gameExes = [...block(migSrc, 'GameExes =').matchAll(/\("([^"]+)",\s*new string\[\d+\]\s*\{([^}]*)\}\)/g)]
  .map(m => ({ profile: m[1], exes: strings(m[2]) }));

// ---- Key labels for calibration (what Windows' GetKeyNameText returns on an English system) ---------
const SC = {
  1: 'Esc', 2: '1', 3: '2', 4: '3', 5: '4', 6: '5', 7: '6', 8: '7', 9: '8', 10: '9', 11: '0',
  12: '-', 13: '=', 14: 'Backspace', 15: 'Tab', 16: 'Q', 17: 'W', 18: 'E', 19: 'R', 20: 'T',
  21: 'Y', 22: 'U', 23: 'I', 24: 'O', 25: 'P', 26: '[', 27: ']', 28: 'Enter', 29: 'Ctrl',
  30: 'A', 31: 'S', 32: 'D', 33: 'F', 34: 'G', 35: 'H', 36: 'J', 37: 'K', 38: 'L', 39: ';',
  40: "'", 41: '`', 42: 'Shift', 43: '\\', 44: 'Z', 45: 'X', 46: 'C', 47: 'V', 48: 'B',
  49: 'N', 50: 'M', 51: ',', 52: '.', 53: '/', 54: 'Right Shift', 55: 'Num *', 56: 'Alt',
  57: 'Space', 58: 'Caps Lock', 59: 'F1', 60: 'F2', 61: 'F3', 62: 'F4', 63: 'F5', 64: 'F6',
  65: 'F7', 66: 'F8', 67: 'F9', 68: 'F10', 69: 'Pause', 70: 'Scroll Lock', 71: 'Num 7',
  72: 'Num 8', 73: 'Num 9', 74: 'Num -', 75: 'Num 4', 76: 'Num 5', 77: 'Num 6', 78: 'Num +',
  79: 'Num 1', 80: 'Num 2', 81: 'Num 3', 82: 'Num 0', 83: 'Num Del', 87: 'F11', 88: 'F12',
  100: 'F13', 101: 'F14', 102: 'F15', 103: 'F16', 104: 'F17', 105: 'F18', 106: 'F19',
  107: 'F20', 108: 'F21', 109: 'F22', 110: 'F23', 118: 'F24'
};
const SC_EXT = {
  28: 'Num Enter', 29: 'Right Ctrl', 53: 'Num /', 55: 'Prnt Scrn', 56: 'Right Alt', 69: 'Num Lock',
  71: 'Home', 72: 'Up', 73: 'Page Up', 75: 'Left', 77: 'Right', 79: 'End', 80: 'Down',
  81: 'Page Down', 82: 'Insert', 83: 'Delete', 91: 'Left Windows', 92: 'Right Windows', 93: 'Application'
};
const scLabel = (sc, ext) => (ext ? SC_EXT[sc] : SC[sc]) || '';

// ---- User data ----------------------------------------------------------------------------------------
const profiles = load('profiles.json', { Mouse: [], Keyboard: [], Macros: [] });
const games = load('games.json', []);
const calib = load('calibration.json', {});
const onboardRaw = load('onboard.json', {});
const settings = load('settings.json', {});
const log = load('log.txt', '');

const lightingFor = (p) => {
  if (p.Lighting) return { effect: String(p.Lighting.Effect || 'static').toLowerCase(), color: (p.Lighting.Color || '#00FF00').toUpperCase(), brightness: p.Lighting.Brightness ?? 100 };
  if (staticColors[p.Name]) return { effect: 'static', color: staticColors[p.Name], brightness: 100 };
  if (spectrum.has(p.Name)) return { effect: 'spectrum', color: '#00FF00', brightness: 100 };
  return null;
};

const mouseProfiles = (profiles.Mouse || []).map(p => {
  const buttons = Object.fromEntries(Object.entries(p.Buttons || {})
    .filter(([, t]) => t && t.Key)
    .map(([id, t]) => [id, { key: t.Key, mods: t.Mods || [] }]));
  for (const f of buttonFixes) if (f.profile === p.Name && !buttons[f.button]) buttons[f.button] = { key: f.key, mods: [] };
  return {
    name: p.Name,
    pollingRate: p.PollingRate ?? 1000,
    dpi: p.Dpi ?? 800,
    dpiStages: p.DpiStages?.length ? p.DpiStages : [p.Dpi ?? 800],
    dpiActive: p.DpiActive ?? 1,
    buttons,
    lighting: lightingFor(p),
    clickSwap: p.ClickSwap ?? true,
    antiCheat: antiCheat.has(String(p.Name).trim().toLowerCase())
  };
});

const keyboardProfiles = (profiles.Keyboard || []).map(p => ({
  name: p.Name,
  brightness: p.Brightness ?? 100,
  snapTap: !!p.SnapTap,
  macroKeys: { ...(p.MacroKeys || {}) },
  effect: p.Effect || 'static',
  effectSpeed: p.EffectSpeed || 2
}));

const macros = (profiles.Macros || []).map(m => ({
  name: m.Name,
  steps: (m.Steps || []).map(s => ({ vk: s.Vk, ext: !!s.Ext, down: !!s.Down, delayMs: s.DelayMs || 0 }))
}));

const gameRows = games.map(g => ({ profile: g.Profile, exes: [...(g.Exes || [])], keyboardProfile: g.KeyboardProfile || '' }));
const claimed = new Set(gameRows.flatMap(g => g.exes.map(e => e.toLowerCase())));
for (const fix of gameExes) {
  const row = gameRows.find(g => g.profile === fix.profile);
  if (!row) continue;
  for (const exe of fix.exes) if (!claimed.has(exe.toLowerCase())) { row.exes.push(exe); claimed.add(exe.toLowerCase()); }
}

const calibration = {};
for (const [id, b] of Object.entries(calib)) {
  const label = scLabel(b.Sc, b.Ext);
  calibration[id] = label || `scan code ${b.Sc}`;
}

const onboard = Object.fromEntries(Object.entries(onboardRaw).map(([k, v]) => [k, typeof v === 'string' ? v : (v?.Name ?? v?.name ?? '')]));

const buttonIds = [...Array.from({ length: 12 }, (_, i) => `Side${i + 1}`), 'Scroll', 'M1', 'M2', 'TiltL', 'TiltR'];

const defaultMouse = settings.DefaultMouse || mouseProfiles[0]?.name || '';
const defaultKeyboard = settings.DefaultKeyboard || keyboardProfiles[0]?.name || '';

const state = {
  version: '1.2.0',
  mouse: { connected: true, name: 'Naga Left-Handed Edition' },
  keyboard: { connected: true, name: 'BlackWidow Lite' },
  auto: true,
  inGame: false,
  liveWrite: settings.LiveWrite ?? true,
  startup: true,
  liveStatus: 'back on slot 3 (Green)',
  activeMouse: defaultMouse,
  activeKeyboard: defaultKeyboard,
  defaultMouse,
  defaultKeyboard,
  mouseProfiles,
  keyboardProfiles,
  macros,
  games: gameRows,
  calibration,
  buttonIds,
  onboard,
  liveSlots: (settings.LiveWrite ?? true) ? [1, 2] : [],
  accent: 'mint',
  competitiveSafe: !!settings.CompetitiveSafe,
  gameKeyboardDefault: settings.GameKeyboardDefault || '',
  lastGameExe: 'eldenring.exe',
  elevatedGame: false,
  razer: {
    checked: true,
    synapseInstalled: false,
    gmsService: 'running',
    filterDrivers: ['RzDev_008d (Naga)', 'RzDev_0235 (BlackWidow Lite)'],
    rzCommon: false
  },
  closeOnGame: true
};

const out = {
  _source: 'Generated by make-mock-data.mjs from LiteSynapse/data, AntiCheat.cs and DataMigrations.cs. Preview only.',
  state,
  runningApps: ['Discord.exe', 'Spotify.exe', 'Steam.exe', 'chrome.exe', 'eldenring.exe', 'explorer.exe', 'obs64.exe', 'steamwebhelper.exe'],
  deviceInfo: { mouse: { dpi: 800, polling: 1000, slot: 3, slotName: 'Green' }, keyboard: { brightness: 7 } },
  lightingZones: [{ id: 0, name: 'All zones' }, { id: 1, name: 'Scroll wheel' }, { id: 2, name: 'Logo' }, { id: 4, name: 'Side panel' }],
  log: String(log).replace(/\r\n/g, '\n')
};

const dest = join(here, 'mock-data.json');
writeFileSync(dest, JSON.stringify(out, null, 2) + '\n');
const lit = mouseProfiles.filter(p => p.lighting).length;
const ac = mouseProfiles.filter(p => p.antiCheat).length;
console.log(`wrote ${dest}: ${mouseProfiles.length} mouse (${lit} with lighting, ${ac} anti-cheat), ${keyboardProfiles.length} keyboard, ` +
  `${macros.length} macros, ${gameRows.length} games, ${Object.keys(calibration).length}/17 calibrated`);
