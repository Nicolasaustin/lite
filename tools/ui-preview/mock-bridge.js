/* LiteSynapse UI preview: in-memory implementation of docs/bridge-api.md (v1.2).
 *
 * Exposes window.__mockBridge = { postMessage(msg), addEventListener('message', fn), removeEventListener(...) },
 * shaped like window.chrome.webview: listeners receive { data: <object> }.
 * Seed data comes from window.__MOCK_DATA__ (tools/ui-preview/mock-data.json), injected before this script.
 * Optional window.__MOCK_OPTS__ = {
 *   latency: ms,                  // reply delay (default 40)
 *   state: { ...overrides },      // shallow overrides of the seed state
 *   fail: ['cmd', ...]            // these commands reply ok:false
 * }
 * Test hooks: __mockBridge.push(obj) sends a push (state/progress/navigate); __mockBridge.state() returns the state.
 * Preview only: never shipped with the app.
 */
(function () {
  'use strict';
  const seed = window.__MOCK_DATA__;
  if (!seed || !seed.state) { console.warn('mock-bridge: window.__MOCK_DATA__ missing'); return; }
  const opts = window.__MOCK_OPTS__ || {};
  const LAT = typeof opts.latency === 'number' ? opts.latency : 40;
  const FAIL = new Set(opts.fail || []);
  const clone = o => JSON.parse(JSON.stringify(o));

  const st = clone(seed.state);
  if (opts.state) Object.assign(st, clone(opts.state));
  let logLines = String(seed.log || '').split('\n').filter(Boolean);
  let listeners = [];

  const SLOT_NAMES = ['', 'White', 'Red', 'Green', 'Blue', 'Cyan'];
  const MACRO_KEYS = ['KEY_UP_ARROW', 'KEY_DOWN_ARROW', 'KEY_LEFT_ARROW', 'KEY_RIGHT_ARROW'];
  const ACCENTS = ['mint', 'green', 'blue', 'violet', 'amber'];
  const KB_EFFECTS = ['off', 'static', 'breathing'];
  const MOUSE_EFFECTS = ['off', 'static', 'breathing', 'spectrum'];
  const ANTI_CHEAT = ['APEXLEGENDS', 'BF', 'Call of Duty', 'CS2', 'fortnite', 'h1z1', 'Halo', 'MarvelRivals',
    'new world', 'PUBG', 'rogue', 'rust', 'splitgate', 'SIEGE', 'Tarky', 'Valorant'].map(s => s.toLowerCase());
  const MODS = ['KEY_LEFT_CTRL', 'KEY_LEFT_SHIFT', 'KEY_LEFT_ALT', 'KEY_LEFT_WIN', 'KEY_RIGHT_CTRL', 'KEY_RIGHT_SHIFT', 'KEY_RIGHT_ALT', 'KEY_RIGHT_WIN'];

  function emit(obj) {
    const msg = { data: clone(obj) };
    listeners.slice().forEach(fn => { try { fn(msg); } catch (e) { console.error(e); } });
  }
  let pushTimer = null;
  function pushState() {
    clearTimeout(pushTimer);
    pushTimer = setTimeout(() => emit({ type: 'state', state: st }), 100);
  }
  function log(text) {
    const d = new Date();
    const p = n => String(n).padStart(2, '0');
    logLines.push(`${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())} ${text}`);
    if (logLines.length > 2000) logLines = logLines.slice(-2000);
  }
  function fail(msg) { throw new Error(msg); }
  function str(v, what) { if (typeof v !== 'string' || !v.trim()) fail(`${what} is empty.`); return v.trim(); }
  function bool(v, what) { if (typeof v !== 'boolean') fail(`"${what}" must be true or false.`); return v; }
  function list(kind) {
    if (kind === 'mouse') return st.mouseProfiles;
    if (kind === 'keyboard') return st.keyboardProfiles;
    return fail('"kind" must be "mouse" or "keyboard".');
  }
  function find(kind, name) {
    const p = list(kind).find(x => x.name === name);
    if (!p) fail(`There is no ${kind} profile named "${name}".`);
    return p;
  }
  const activeMouse = () => st.mouseProfiles.find(p => p.name === st.activeMouse) || null;
  const activeKb = () => st.keyboardProfiles.find(p => p.name === st.activeKeyboard) || null;
  const isProtected = name => ANTI_CHEAT.includes(String(name || '').trim().toLowerCase());
  function updateLive() {
    st.liveSlots = st.liveWrite ? [1, 2] : [];
    if (!st.liveWrite) st.liveStatus = '';
    else if (st.inGame || !st.auto) st.liveStatus = `"${st.activeMouse}" is running on the mouse (slot 1, White)`;
    else st.liveStatus = 'back on slot 3 (Green)';
  }
  function uniqueName(kind, base) {
    const names = list(kind).map(p => p.name.toLowerCase());
    let n = `${base} copy`, i = 2;
    while (names.includes(n.toLowerCase())) n = `${base} copy ${i++}`;
    return n;
  }
  const delay = ms => new Promise(r => setTimeout(r, ms));
  const game = profile => st.games.find(g => g.profile === profile) || fail(`"${profile}" is not linked to a game.`);

  const handlers = {
    getState: () => st,
    getDeviceInfo: () => delay(650).then(() => {
      const mp = activeMouse(), kb = activeKb();
      const slot = st.liveWrite && (st.inGame || !st.auto) ? 1 : 3;
      return {
        mouse: st.mouse.connected ? { dpi: mp ? mp.dpi : 800, polling: mp ? mp.pollingRate : 1000, slot, slotName: SLOT_NAMES[slot] } : null,
        keyboard: st.keyboard.connected ? { brightness: kb ? kb.brightness : 100 } : null
      };
    }),
    selectProfile: a => {
      const p = find(a.kind, a.name);
      if (a.kind === 'mouse') st.activeMouse = p.name; else st.activeKeyboard = p.name;
      st.auto = false; updateLive();
      log(`profile -> ${st.activeMouse} / ${st.activeKeyboard}`);
      return st;
    },
    setAuto: a => {
      st.auto = bool(a.on, 'on');
      if (st.auto) { st.inGame = false; st.activeMouse = st.defaultMouse; st.activeKeyboard = st.defaultKeyboard; }
      updateLive(); log(`auto ${st.auto}`);
      return st;
    },
    setLiveWrite: a => { st.liveWrite = bool(a.on, 'on'); updateLive(); return st; },
    setStartup: a => { st.startup = bool(a.on, 'on'); return st; },
    setDpiStages: a => {
      const mp = activeMouse() || fail('No mouse profile is active.');
      if (!Array.isArray(a.stages) || a.stages.length < 1 || a.stages.length > 5) fail('Use 1 to 5 DPI stages.');
      const s = a.stages.map(v => { v = Math.round(Number(v)); if (!(v >= 100 && v <= 20000)) fail('"stages" must be between 100 and 20000.'); return v; });
      const act = Math.round(Number(a.active));
      if (!(act >= 1 && act <= s.length)) fail(`"active" must be between 1 and ${s.length}.`);
      mp.dpiStages = s; mp.dpiActive = act; mp.dpi = s[act - 1];
      log(`dpi ${mp.dpi}`);
      return st;
    },
    setPolling: a => {
      const mp = activeMouse() || fail('No mouse profile is active.');
      if (![125, 500, 1000].includes(a.hz)) fail('The polling rate must be 125, 500 or 1000 Hz.');
      mp.pollingRate = a.hz; return st;
    },
    setButton: a => {
      const mp = activeMouse() || fail('No mouse profile is active.');
      if (!st.buttonIds.includes(a.id)) fail(`Unknown button "${a.id}".`);
      const buttons = Object.assign({}, mp.buttons);
      if (!a.key) delete buttons[a.id];
      else {
        if (a.key !== 'DISABLED' && a.key !== 'MOUSE_MIDDLE' && !/^KEY_[A-Z0-9_]+$/.test(a.key)) fail(`Unknown key "${a.key}".`);
        const mods = Array.isArray(a.mods) ? a.mods : [];
        mods.forEach(m => { if (!MODS.includes(m)) fail(`Unknown modifier "${m}".`); });
        buttons[a.id] = { key: a.key, mods: a.key.startsWith('KEY_') ? mods.slice() : [] };
      }
      mp.buttons = buttons;
      log(`button ${a.id} -> ${a.key || 'default'}`);
      return st;
    },
    setKbBrightness: a => {
      const kb = activeKb() || fail('No keyboard profile is active.');
      const v = Math.round(Number(a.value)); if (!(v >= 0 && v <= 100)) fail('"value" must be between 0 and 100.');
      kb.brightness = v; log(`kb brightness ${v}:True`); return st;
    },
    setKbEffect: a => {
      const kb = activeKb() || fail('No keyboard profile is active.');
      if (!KB_EFFECTS.includes(a.effect)) fail(`Unknown effect "${a.effect}".`);
      const sp = Math.round(Number(a.speed)); if (!(sp >= 1 && sp <= 3)) fail('"speed" must be between 1 and 3.');
      kb.effect = a.effect; kb.effectSpeed = sp; return st;
    },
    setSnapTap: a => { const kb = activeKb() || fail('No keyboard profile is active.'); kb.snapTap = bool(a.on, 'on'); return st; },
    setMacroKey: a => {
      const kb = activeKb() || fail('No keyboard profile is active.');
      if (!MACRO_KEYS.includes(a.key)) fail('Only the arrow keys can run macros.');
      const keys = Object.assign({}, kb.macroKeys);
      if (!a.macro) delete keys[a.key];
      else {
        if (!st.macros.some(m => m.name === a.macro)) fail(`There is no macro named "${a.macro}".`);
        keys[a.key] = a.macro;
      }
      kb.macroKeys = keys;
      return st;
    },
    addProfile: a => {
      const name = str(a.name, 'Name');
      if (list(a.kind).some(p => p.name.toLowerCase() === name.toLowerCase())) fail(`A profile named "${name}" already exists.`);
      if (a.kind === 'mouse') st.mouseProfiles.push({ name, pollingRate: 1000, dpi: 800, dpiStages: [400, 800, 1600], dpiActive: 2, buttons: {}, lighting: null, clickSwap: true, antiCheat: isProtected(name) });
      else st.keyboardProfiles.push({ name, brightness: 100, snapTap: false, macroKeys: {}, effect: 'static', effectSpeed: 2 });
      return st;
    },
    renameProfile: a => {
      const p = find(a.kind, a.from), to = str(a.to, 'New name');
      if (to !== p.name && list(a.kind).some(x => x !== p && x.name.toLowerCase() === to.toLowerCase())) fail(`A profile named "${to}" already exists.`);
      const from = p.name; p.name = to;
      if (a.kind === 'mouse') {
        p.antiCheat = isProtected(to);
        st.games.forEach(g => { if (g.profile === from) g.profile = to; });
        if (st.activeMouse === from) st.activeMouse = to;
        if (st.defaultMouse === from) st.defaultMouse = to;
        Object.keys(st.onboard).forEach(k => { if (st.onboard[k] === from) st.onboard[k] = to; });
      } else {
        st.games.forEach(g => { if (g.keyboardProfile === from) g.keyboardProfile = to; });
        if (st.activeKeyboard === from) st.activeKeyboard = to;
        if (st.defaultKeyboard === from) st.defaultKeyboard = to;
        if (st.gameKeyboardDefault === from) st.gameKeyboardDefault = to;
      }
      updateLive();
      return st;
    },
    duplicateProfile: a => {
      const p = find(a.kind, a.name), c = clone(p);
      c.name = uniqueName(a.kind, p.name);
      if (a.kind === 'mouse') c.antiCheat = isProtected(c.name);
      const arr = list(a.kind); arr.splice(arr.indexOf(p) + 1, 0, c);
      pushState();
      return { name: c.name };
    },
    deleteProfile: a => {
      const p = find(a.kind, a.name);
      if ((a.kind === 'mouse' ? st.defaultMouse : st.defaultKeyboard) === p.name) fail(`"${p.name}" is the starting profile. Pick another starting profile first.`);
      const arr = list(a.kind); arr.splice(arr.indexOf(p), 1);
      if (a.kind === 'mouse') {
        st.games = st.games.filter(g => g.profile !== p.name);
        if (st.activeMouse === p.name) st.activeMouse = st.defaultMouse;
      } else {
        st.games.forEach(g => { if (g.keyboardProfile === p.name) g.keyboardProfile = ''; });
        if (st.activeKeyboard === p.name) st.activeKeyboard = st.defaultKeyboard;
        if (st.gameKeyboardDefault === p.name) st.gameKeyboardDefault = '';
      }
      updateLive();
      return st;
    },
    resetProfile: a => {
      const p = find(a.kind, a.name);
      if (a.kind === 'mouse') p.buttons = {}; else p.macroKeys = {};
      return st;
    },
    setStartingProfile: a => {
      const p = find(a.kind, a.name);
      if (a.kind === 'mouse') st.defaultMouse = p.name; else st.defaultKeyboard = p.name;
      return st;
    },
    exportProfile: a => { const p = find(a.kind, a.name); return delay(300).then(() => ({ path: `C:\\Users\\nick\\Documents\\${p.name}.synapse4` })); },
    importProfiles: () => delay(300).then(() => ({ message: 'Nothing was imported (preview mode).' })),
    addGame: a => {
      const p = find('mouse', a.profile);
      if (st.games.some(g => g.profile === p.name)) fail(`"${p.name}" is already linked.`);
      st.games.push({ profile: p.name, exes: [], keyboardProfile: '' });
      return st;
    },
    setGameExes: a => {
      find('mouse', a.profile);
      if (!Array.isArray(a.exes)) fail('"exes" must be a list of text values.');
      // Same cleanup as WebOps.CleanExes: path -> file name, "cs2" -> "cs2.exe", duplicates kept once.
      const exes = [];
      a.exes.forEach(raw => {
        let e = String(raw == null ? '' : raw).trim().replace(/^"+|"+$/g, '').trim().replace(/\//g, '\\');
        e = e.slice(e.lastIndexOf('\\') + 1).trim();
        if (!e) return;
        if (e.length > 255 || /[<>:"/\\|?*\x00-\x1f]/.test(e)) fail(`"${raw}" is not a program file name.`);
        if (!e.includes('.')) e += '.exe';
        if (!exes.some(y => y.toLowerCase() === e.toLowerCase())) exes.push(e);
      });
      if (exes.length > 50) fail('A game can have at most 50 programs.');
      let g = st.games.find(x => x.profile === a.profile);
      if (!g) { g = { profile: a.profile, exes: [], keyboardProfile: '' }; st.games.push(g); }
      g.exes = exes;
      return st;
    },
    setGameKeyboard: a => {
      const g = game(a.profile);
      if (a.keyboardProfile) find('keyboard', a.keyboardProfile);
      g.keyboardProfile = a.keyboardProfile || '';
      return st;
    },
    removeGame: a => { game(a.profile); st.games = st.games.filter(g => g.profile !== a.profile); return st; },
    runningApps: () => delay(250).then(() => (seed.runningApps || []).slice().sort((x, y) => x.toLowerCase().localeCompare(y.toLowerCase()))),
    saveMacro: a => {
      const m = a.macro || {};
      const name = str(m.name, 'Macro name');
      if (!Array.isArray(m.steps)) fail('"steps" must be a list.');
      const steps = m.steps.map(s => {
        const d = Math.round(Number(s.delayMs) || 0);
        if (d < 0 || d > 60000) fail('A step delay must be between 0 and 60000 ms.');
        return { vk: s.vk | 0, ext: !!s.ext, down: !!s.down, delayMs: d };
      });
      const orig = a.originalName;
      if (st.macros.some(x => x.name.toLowerCase() === name.toLowerCase() && x.name !== orig)) fail(`A macro named "${name}" already exists.`);
      if (orig) {
        const ex = st.macros.find(x => x.name === orig) || fail(`There is no macro named "${orig}".`);
        ex.name = name; ex.steps = steps;
        if (orig !== name) st.keyboardProfiles.forEach(kp => Object.keys(kp.macroKeys).forEach(k => { if (kp.macroKeys[k] === orig) kp.macroKeys[k] = name; }));
      } else st.macros.push({ name, steps });
      return st;
    },
    deleteMacro: a => {
      if (!st.macros.some(m => m.name === a.name)) fail(`There is no macro named "${a.name}".`);
      st.macros = st.macros.filter(m => m.name !== a.name);
      st.keyboardProfiles.forEach(kp => Object.keys(kp.macroKeys).forEach(k => { if (kp.macroKeys[k] === a.name) delete kp.macroKeys[k]; }));
      return st;
    },
    onboardWrite: a => {
      const slot = a.slot | 0;
      if (slot < 2 || slot > 5) fail('"slot" must be between 2 and 5.');
      if (st.liveSlots.includes(slot)) fail(`Slot ${slot} is used for live profiles.`);
      const p = find('mouse', a.profile);
      const steps = [`Writing "${p.name}" to slot ${slot}...`, 'Writing DPI and polling rate...', 'Writing buttons...', 'Writing lighting...', 'Checking what was written...'];
      let chain = Promise.resolve();
      steps.forEach(t => { chain = chain.then(() => { emit({ type: 'progress', op: 'onboard', text: t }); return delay(420); }); });
      return chain.then(() => {
        st.onboard[String(slot)] = p.name; pushState();
        log(`onboard slot ${slot} <- ${p.name}: ok`);
        return { ok: true, message: `Wrote "${p.name}" to slot ${slot} (${SLOT_NAMES[slot]}). Press the profile button under the wheel to switch to it.` };
      });
    },
    calibrate: () => null,
    probeMouseLighting: () => delay(500).then(() => ({ zones: clone(seed.lightingZones || [{ id: 0, name: 'All zones' }]) })),
    setMouseLighting: a => {
      const p = a.profile ? find('mouse', a.profile) : (activeMouse() || fail('No mouse profile is active.'));
      if (!MOUSE_EFFECTS.includes(a.effect)) fail(`Unknown effect "${a.effect}".`);
      if (!/^#[0-9A-Fa-f]{6}$/.test(a.color || '')) fail('"color" must be #RRGGBB.');
      const b = Math.round(Number(a.brightness)); if (!(b >= 0 && b <= 100)) fail('"brightness" must be between 0 and 100.');
      p.lighting = { effect: a.effect, color: a.color.toUpperCase(), brightness: b };
      return st;
    },
    setCompetitiveSafe: a => { st.competitiveSafe = bool(a.on, 'on'); return st; },
    setGameKeyboardDefault: a => {
      if (a.keyboardProfile) find('keyboard', a.keyboardProfile);
      st.gameKeyboardDefault = a.keyboardProfile || '';
      return st;
    },
    linkRunningGame: a => {
      const p = find('mouse', a.profile);
      const exe = st.lastGameExe || null;
      if (!exe) return { exe: null };
      let g = st.games.find(x => x.profile === p.name);
      if (!g) { g = { profile: p.name, exes: [], keyboardProfile: '' }; st.games.push(g); }
      st.games.forEach(x => { if (x !== g) x.exes = x.exes.filter(e => e.toLowerCase() !== exe.toLowerCase()); });
      if (!g.exes.some(e => e.toLowerCase() === exe.toLowerCase())) g.exes.push(exe);
      pushState();
      return { exe };
    },
    razerCheck: () => delay(700).then(() => { st.razer = Object.assign({}, st.razer, { checked: true }); return st; }),
    razerCleanup: a => {
      if (a.action === 'disableGameManager') return delay(600).then(() => { st.razer = Object.assign({}, st.razer, { gmsService: 'disabled' }); pushState(); return { message: 'Razer Game Manager Service 3 is stopped and disabled.' }; });
      if (a.action === 'driverGuide') return delay(400).then(() => ({ message: 'Opened the driver removal guide in an administrator PowerShell window (dry run first).' }));
      return fail(`Unknown action "${a.action}".`);
    },
    runSelfTest: () => null,
    openConfigFolder: () => null,
    reloadConfig: () => st,
    openMouseProperties: () => null,
    openClassicWindow: () => null,
    getLog: a => { const n = (a && a.lines) || 200; return logLines.slice(-n).join('\n'); },
    setAccent: a => { if (!ACCENTS.includes(a.accent)) fail(`Unknown accent "${a.accent}".`); st.accent = a.accent; return st; },
    setCloseOnGame: a => { st.closeOnGame = bool(a.on, 'on'); return st; }
  };
  // Commands whose reply is followed by a state push (contract: "after every mutating command").
  const MUTATING = /^(select|set|add|rename|delete|reset|remove|save|reload|razerCheck)/;

  function handle(msg) {
    if (typeof msg === 'string') { try { msg = JSON.parse(msg); } catch (e) { return; } }
    if (!msg || typeof msg.id !== 'number') { console.error('mock-bridge: bad message', msg); return; }
    const fn = handlers[msg.cmd];
    if (!fn) { emit({ type: 'reply', id: msg.id, ok: false, error: `Unknown command: ${msg.cmd}` }); return; }
    if (FAIL.has(msg.cmd)) { emit({ type: 'reply', id: msg.id, ok: false, error: `${msg.cmd} failed (preview: simulated error).` }); return; }
    let out;
    try { out = fn(msg.args || {}); } catch (e) { emit({ type: 'reply', id: msg.id, ok: false, error: e.message }); return; }
    Promise.resolve(out).then(res => {
      emit({ type: 'reply', id: msg.id, ok: true, result: res === undefined ? null : res });
      if (MUTATING.test(msg.cmd)) pushState();
    }, e => emit({ type: 'reply', id: msg.id, ok: false, error: e.message }));
  }

  window.__mockBridge = {
    isMock: true,
    commands: Object.keys(handlers),
    postMessage(msg) { const copy = clone(msg); setTimeout(() => handle(copy), LAT); },
    addEventListener(type, fn) { if (type === 'message' && typeof fn === 'function') listeners.push(fn); },
    removeEventListener(type, fn) { listeners = listeners.filter(f => f !== fn); },
    push(obj) { emit(obj); },
    state() { return st; }
  };
})();
