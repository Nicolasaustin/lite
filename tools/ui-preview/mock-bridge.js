/* LiteSynapse UI preview — in-memory implementation of docs/bridge-api.md (v1.1).
 *
 * Exposes window.__mockBridge = { postMessage(msg), addEventListener('message', fn), removeEventListener }.
 * Messages delivered to listeners look like WebView2's: { data: <object> }.
 * Seed data comes from window.__MOCK_DATA__ (tools/ui-preview/mock-data.json), injected before this script.
 * Optional window.__MOCK_OPTS__ = { latency: ms, state: { ...overrides } }.
 * Preview only: never shipped with the app.
 */
(function () {
  'use strict';
  var seed = window.__MOCK_DATA__;
  if (!seed || !seed.state) { console.warn('mock-bridge: window.__MOCK_DATA__ missing'); return; }
  var opts = window.__MOCK_OPTS__ || {};
  var LAT = typeof opts.latency === 'number' ? opts.latency : 40;
  var clone = function (o) { return JSON.parse(JSON.stringify(o)); };

  var st = clone(seed.state);
  if (opts.state) Object.keys(opts.state).forEach(function (k) { st[k] = opts.state[k]; });
  var logLines = String(seed.log || '').split('\n').filter(Boolean);
  var listeners = [];
  var SLOT_NAMES = ['', 'White', 'Red', 'Green', 'Blue', 'Cyan'];
  var MACRO_KEYS = ['KEY_UP_ARROW', 'KEY_DOWN_ARROW', 'KEY_LEFT_ARROW', 'KEY_RIGHT_ARROW'];
  var ACCENTS = ['mint', 'green', 'blue', 'violet', 'amber'];
  var KB_EFFECTS = ['off', 'static', 'breathing', 'reactive', 'starlight'];
  var MOUSE_EFFECTS = ['off', 'static', 'breathing', 'spectrum'];

  function emit(obj) {
    var msg = { data: clone(obj) };
    listeners.slice().forEach(function (fn) { try { fn(msg); } catch (e) { console.error(e); } });
  }
  var pushTimer = null;
  function pushState() {
    clearTimeout(pushTimer);
    pushTimer = setTimeout(function () { emit({ type: 'state', state: st }); }, 100);
  }
  function log(text) {
    var d = new Date();
    var p = function (n) { return (n < 10 ? '0' : '') + n; };
    logLines.push(p(d.getHours()) + ':' + p(d.getMinutes()) + ':' + p(d.getSeconds()) + ' ' + text);
    if (logLines.length > 2000) logLines.splice(0, logLines.length - 2000);
  }
  function fail(msg) { var e = new Error(msg); e.bridge = true; throw e; }
  function str(v, what) { if (typeof v !== 'string' || !v.trim()) fail(what + ' is required'); return v.trim(); }
  function list(kind) {
    if (kind === 'mouse') return st.mouseProfiles;
    if (kind === 'keyboard') return st.keyboardProfiles;
    fail('kind must be "mouse" or "keyboard"');
  }
  function find(kind, name) {
    var p = list(kind).filter(function (x) { return x.name === name; })[0];
    if (!p) fail('No ' + kind + ' profile named "' + name + '"');
    return p;
  }
  function activeMouse() { return st.mouseProfiles.filter(function (p) { return p.name === st.activeMouse; })[0] || null; }
  function activeKb() { return st.keyboardProfiles.filter(function (p) { return p.name === st.activeKeyboard; })[0] || null; }
  function liveSlots() { st.liveSlots = st.liveWrite ? [1, 2] : []; }
  function updateLive() {
    if (st.liveWrite && st.inGame && st.activeMouse) st.liveStatus = '"' + st.activeMouse + '" is running on the mouse (slot 1, White)';
    else if (st.liveWrite) st.liveStatus = 'back on slot 3 (Green)';
    else st.liveStatus = '';
  }
  function uniqueName(kind, base) {
    var names = list(kind).map(function (p) { return p.name.toLowerCase(); });
    var n = base + ' copy', i = 2;
    while (names.indexOf(n.toLowerCase()) >= 0) n = base + ' copy ' + (i++);
    return n;
  }
  function delay(ms) { return new Promise(function (r) { setTimeout(r, ms); }); }

  var handlers = {
    getState: function () { return st; },
    getDeviceInfo: function () {
      return delay(650).then(function () {
        var mp = activeMouse(), kb = activeKb();
        var slot = st.liveWrite && st.inGame ? 1 : 3;
        return {
          mouse: st.mouse.connected ? { dpi: mp ? mp.dpi : 800, polling: mp ? mp.pollingRate : 1000, slot: slot, slotName: SLOT_NAMES[slot] } : null,
          keyboard: st.keyboard.connected ? { brightness: kb ? kb.brightness : 100 } : null
        };
      });
    },
    selectProfile: function (a) {
      var p = find(a.kind, a.name);
      if (a.kind === 'mouse') st.activeMouse = p.name; else st.activeKeyboard = p.name;
      st.auto = false; st.inGame = true; updateLive();
      log('profile -> ' + st.activeMouse + ' / ' + st.activeKeyboard);
      return st;
    },
    setAuto: function (a) { st.auto = !!a.on; if (!st.auto) { /* keep */ } else { st.inGame = false; st.activeMouse = st.defaultMouse; st.activeKeyboard = st.defaultKeyboard; } updateLive(); log('auto ' + st.auto); return st; },
    setLiveWrite: function (a) { st.liveWrite = !!a.on; liveSlots(); updateLive(); return st; },
    setStartup: function (a) { st.startup = !!a.on; return st; },
    setDpiStages: function (a) {
      var mp = activeMouse(); if (!mp) fail('No active mouse profile');
      var s = a.stages;
      if (!Array.isArray(s) || s.length < 1 || s.length > 5) fail('Use 1 to 5 DPI stages');
      s = s.map(function (v) { v = Math.round(Number(v)); if (!(v >= 100 && v <= 20000)) fail('DPI must be 100–20000'); return v; });
      var act = Math.round(Number(a.active));
      if (!(act >= 1 && act <= s.length)) fail('Active stage out of range');
      mp.dpiStages = s; mp.dpiActive = act; mp.dpi = s[act - 1];
      log('dpi ' + mp.dpi);
      return st;
    },
    setPolling: function (a) {
      var mp = activeMouse(); if (!mp) fail('No active mouse profile');
      if ([125, 500, 1000].indexOf(a.hz) < 0) fail('Polling rate must be 125, 500 or 1000');
      mp.pollingRate = a.hz; return st;
    },
    setButton: function (a) {
      var mp = activeMouse(); if (!mp) fail('No active mouse profile');
      if (st.buttonIds.indexOf(a.id) < 0) fail('Unknown button ' + a.id);
      if (!a.key) delete mp.buttons[a.id];
      else {
        if (!/^KEY_[A-Z0-9_]+$/.test(a.key)) fail('Unknown key ' + a.key);
        mp.buttons[a.id] = { key: a.key, mods: Array.isArray(a.mods) ? a.mods.slice() : [] };
      }
      log('button ' + a.id + ' -> ' + (a.key || 'default'));
      return st;
    },
    setKbBrightness: function (a) {
      var kb = activeKb(); if (!kb) fail('No active keyboard profile');
      var v = Math.round(Number(a.value)); if (!(v >= 0 && v <= 100)) fail('Brightness must be 0–100');
      kb.brightness = v; log('kb brightness ' + v + ':True'); return st;
    },
    setKbEffect: function (a) {
      var kb = activeKb(); if (!kb) fail('No active keyboard profile');
      if (KB_EFFECTS.indexOf(a.effect) < 0) fail('Unknown effect ' + a.effect);
      var sp = Math.round(Number(a.speed)); if (!(sp >= 1 && sp <= 3)) fail('Speed must be 1–3');
      kb.effect = a.effect; kb.effectSpeed = sp; return st;
    },
    setSnapTap: function (a) { var kb = activeKb(); if (!kb) fail('No active keyboard profile'); kb.snapTap = !!a.on; return st; },
    setMacroKey: function (a) {
      var kb = activeKb(); if (!kb) fail('No active keyboard profile');
      if (MACRO_KEYS.indexOf(a.key) < 0) fail('Only arrow keys can run macros');
      if (!a.macro) delete kb.macroKeys[a.key];
      else {
        if (!st.macros.some(function (m) { return m.name === a.macro; })) fail('No macro named "' + a.macro + '"');
        kb.macroKeys[a.key] = a.macro;
      }
      return st;
    },
    addProfile: function (a) {
      var name = str(a.name, 'Name');
      if (list(a.kind).some(function (p) { return p.name.toLowerCase() === name.toLowerCase(); })) fail('A profile named "' + name + '" already exists');
      if (a.kind === 'mouse') st.mouseProfiles.push({ name: name, pollingRate: 1000, dpi: 800, dpiStages: [400, 800, 1600], dpiActive: 2, buttons: {} });
      else st.keyboardProfiles.push({ name: name, brightness: 100, snapTap: false, macroKeys: {}, effect: 'static', effectSpeed: 2 });
      return st;
    },
    renameProfile: function (a) {
      var p = find(a.kind, a.from), to = str(a.to, 'New name');
      if (to !== p.name && list(a.kind).some(function (x) { return x.name.toLowerCase() === to.toLowerCase(); })) fail('A profile named "' + to + '" already exists');
      var from = p.name; p.name = to;
      if (a.kind === 'mouse') {
        st.games.forEach(function (g) { if (g.profile === from) g.profile = to; });
        if (st.activeMouse === from) st.activeMouse = to;
        if (st.defaultMouse === from) st.defaultMouse = to;
        Object.keys(st.onboard).forEach(function (k) { if (st.onboard[k] === from) st.onboard[k] = to; });
      } else {
        st.games.forEach(function (g) { if (g.keyboardProfile === from) g.keyboardProfile = to; });
        if (st.activeKeyboard === from) st.activeKeyboard = to;
        if (st.defaultKeyboard === from) st.defaultKeyboard = to;
      }
      updateLive();
      return st;
    },
    duplicateProfile: function (a) {
      var p = find(a.kind, a.name), c = clone(p);
      c.name = uniqueName(a.kind, p.name);
      var arr = list(a.kind); arr.splice(arr.indexOf(p) + 1, 0, c);
      pushState();
      return { name: c.name };
    },
    deleteProfile: function (a) {
      var p = find(a.kind, a.name);
      if (a.kind === 'mouse' && st.defaultMouse === p.name || a.kind === 'keyboard' && st.defaultKeyboard === p.name) fail('"' + p.name + '" is the starting profile. Pick another starting profile first.');
      var arr = list(a.kind); arr.splice(arr.indexOf(p), 1);
      if (a.kind === 'mouse') {
        st.games = st.games.filter(function (g) { return g.profile !== p.name; });
        if (st.activeMouse === p.name) st.activeMouse = st.defaultMouse;
      } else {
        st.games.forEach(function (g) { if (g.keyboardProfile === p.name) g.keyboardProfile = ''; });
        if (st.activeKeyboard === p.name) st.activeKeyboard = st.defaultKeyboard;
      }
      updateLive();
      return st;
    },
    resetProfile: function (a) {
      var p = find(a.kind, a.name);
      if (a.kind === 'mouse') p.buttons = {}; else p.macroKeys = {};
      return st;
    },
    setStartingProfile: function (a) {
      var p = find(a.kind, a.name);
      if (a.kind === 'mouse') st.defaultMouse = p.name; else st.defaultKeyboard = p.name;
      return st;
    },
    exportProfile: function (a) {
      var p = find(a.kind, a.name);
      return delay(300).then(function () { return { path: 'C:\\Users\\nick\\Documents\\' + p.name + '.synapse4' }; });
    },
    importProfiles: function () { return delay(300).then(function () { return { message: 'Nothing was imported (preview mode).' }; }); },
    addGame: function (a) {
      var p = find('mouse', a.profile);
      if (st.games.some(function (g) { return g.profile === p.name; })) fail('"' + p.name + '" is already linked');
      st.games.push({ profile: p.name, exes: [], keyboardProfile: '' });
      return st;
    },
    setGameExes: function (a) {
      var g = st.games.filter(function (x) { return x.profile === a.profile; })[0]; if (!g) fail('No game link for "' + a.profile + '"');
      if (!Array.isArray(a.exes)) fail('exes must be a list');
      g.exes = a.exes.map(function (e) { return String(e).trim(); }).filter(Boolean);
      return st;
    },
    setGameKeyboard: function (a) {
      var g = st.games.filter(function (x) { return x.profile === a.profile; })[0]; if (!g) fail('No game link for "' + a.profile + '"');
      if (a.keyboardProfile) find('keyboard', a.keyboardProfile);
      g.keyboardProfile = a.keyboardProfile || '';
      return st;
    },
    removeGame: function (a) {
      var n = st.games.length;
      st.games = st.games.filter(function (g) { return g.profile !== a.profile; });
      if (st.games.length === n) fail('No game link for "' + a.profile + '"');
      return st;
    },
    runningApps: function () { return delay(250).then(function () { return (seed.runningApps || []).slice().sort(function (a, b) { return a.toLowerCase() < b.toLowerCase() ? -1 : 1; }); }); },
    saveMacro: function (a) {
      var m = a.macro || {}; var name = str(m.name, 'Macro name');
      if (!Array.isArray(m.steps)) fail('steps must be a list');
      var steps = m.steps.map(function (s) { return { vk: s.vk | 0, ext: !!s.ext, down: !!s.down, delayMs: Math.max(0, s.delayMs | 0) }; });
      var orig = a.originalName;
      if (st.macros.some(function (x) { return x.name.toLowerCase() === name.toLowerCase() && x.name !== orig; })) fail('A macro named "' + name + '" already exists');
      if (orig) {
        var ex = st.macros.filter(function (x) { return x.name === orig; })[0]; if (!ex) fail('No macro named "' + orig + '"');
        ex.name = name; ex.steps = steps;
        if (orig !== name) st.keyboardProfiles.forEach(function (kp) { Object.keys(kp.macroKeys).forEach(function (k) { if (kp.macroKeys[k] === orig) kp.macroKeys[k] = name; }); });
      } else st.macros.push({ name: name, steps: steps });
      return st;
    },
    deleteMacro: function (a) {
      var n = st.macros.length;
      st.macros = st.macros.filter(function (m) { return m.name !== a.name; });
      if (st.macros.length === n) fail('No macro named "' + a.name + '"');
      st.keyboardProfiles.forEach(function (kp) { Object.keys(kp.macroKeys).forEach(function (k) { if (kp.macroKeys[k] === a.name) delete kp.macroKeys[k]; }); });
      return st;
    },
    onboardWrite: function (a) {
      var slot = a.slot | 0;
      if (slot < 2 || slot > 5) fail('Slot must be 2–5');
      if (st.liveSlots.indexOf(slot) >= 0) fail('Slot ' + slot + ' is used for live profiles');
      var p = find('mouse', a.profile);
      var steps = ['Erasing slot ' + slot + '...', 'Writing DPI and polling...', 'Writing buttons...', 'Verifying...'];
      var chain = Promise.resolve();
      steps.forEach(function (t) { chain = chain.then(function () { emit({ type: 'progress', op: 'onboard', text: t }); return delay(450); }); });
      return chain.then(function () {
        st.onboard[String(slot)] = p.name; pushState();
        log('onboard slot ' + slot + ' <- ' + p.name + ': ok');
        return { ok: true, message: 'Wrote "' + p.name + '" to slot ' + slot + ' (' + SLOT_NAMES[slot] + '). Press Cycle Up Profile on the mouse to use it.' };
      });
    },
    calibrate: function () { return null; },
    probeMouseLighting: function () { return delay(500).then(function () { return { zones: clone(seed.lightingZones || [{ id: 0, name: 'All zones' }]) }; }); },
    setMouseLighting: function (a) {
      if (MOUSE_EFFECTS.indexOf(a.effect) < 0) fail('Unknown effect ' + a.effect);
      if (!/^#[0-9A-Fa-f]{6}$/.test(a.color || '')) fail('Color must be #RRGGBB');
      var b = Math.round(Number(a.brightness)); if (!(b >= 0 && b <= 100)) fail('Brightness must be 0–100');
      if (!Array.isArray(a.zones)) fail('zones must be a list');
      st.mouseLighting = { effect: a.effect, color: a.color.toUpperCase(), brightness: b, zones: a.zones.slice() };
      return st;
    },
    openConfigFolder: function () { return null; },
    reloadConfig: function () { return st; },
    openMouseProperties: function () { return null; },
    openClassicWindow: function () { return null; },
    getLog: function (a) { var n = (a && a.lines) || 200; return logLines.slice(-n).join('\n'); },
    setAccent: function (a) { if (ACCENTS.indexOf(a.accent) < 0) fail('Unknown accent ' + a.accent); st.accent = a.accent; return st; }
  };
  var MUTATING = /^(select|set|add|rename|delete|reset|remove|save|reload)/;

  function handle(msg) {
    if (typeof msg === 'string') { try { msg = JSON.parse(msg); } catch (e) { return; } }
    if (!msg || typeof msg.id !== 'number') { console.error('mock-bridge: bad message', msg); return; }
    var fn = handlers[msg.cmd];
    if (!fn) { emit({ type: 'reply', id: msg.id, ok: false, error: 'Unknown command: ' + msg.cmd }); return; }
    var out;
    try { out = fn(msg.args || {}); } catch (e) { emit({ type: 'reply', id: msg.id, ok: false, error: e.message }); return; }
    Promise.resolve(out).then(function (res) {
      emit({ type: 'reply', id: msg.id, ok: true, result: res === undefined ? null : res });
      if (MUTATING.test(msg.cmd)) pushState();
    }, function (e) { emit({ type: 'reply', id: msg.id, ok: false, error: e.message }); });
  }

  window.__mockBridge = {
    isMock: true,
    postMessage: function (msg) { setTimeout(function () { handle(clone(msg)); }, LAT); },
    addEventListener: function (type, fn) { if (type === 'message' && typeof fn === 'function') listeners.push(fn); },
    removeEventListener: function (type, fn) { listeners = listeners.filter(function (f) { return f !== fn; }); },
    _state: function () { return st; }
  };
})();
