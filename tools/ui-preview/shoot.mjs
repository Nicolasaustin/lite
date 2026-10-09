// Screenshots of the LiteSynapse window (src/LiteSynapse/ui/index.html) with the mock bridge, for review.
//
//   cd tools/ui-preview && node shoot.mjs [--only <substring>]
//
// Loads the page from file:// in Chromium (Playwright), injects mock-data.json + mock-bridge.js before the page's own
// script, walks every page, tab and the main interactions, and writes docs/screenshots/*.png.
// Fails (exit code 1) on any console error or warning, page error, failed interaction, a contract command the mock
// does not implement, or a command the page sends that the contract does not list.
// Preview only: nothing here ships with the app.
import { chromium } from 'playwright';
import { readFileSync, mkdirSync, readdirSync, rmSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const repo = resolve(here, '..', '..');
const pagePath = join(repo, 'src', 'LiteSynapse', 'ui', 'index.html');
const pageUrl = pathToFileURL(pagePath).href;
const outDir = join(repo, 'docs', 'screenshots');
const data = JSON.parse(readFileSync(join(here, 'mock-data.json'), 'utf8'));
const bridgeSrc = readFileSync(join(here, 'mock-bridge.js'), 'utf8');
const only = process.argv.includes('--only') ? process.argv[process.argv.indexOf('--only') + 1] : '';

const problems = [];
const idleReport = [];
const shots = [];

// ---- Static checks ---------------------------------------------------------------------------------------
const contract = readFileSync(join(repo, 'docs', 'bridge-api.md'), 'utf8');
const contractCmds = [...contract.matchAll(/^\|\s*`([A-Za-z]+)`\s*\|/gm)].map(m => m[1]);
// Commands the C# bridge implements beyond the contract table (none: setCloseOnGame is documented now).
const EXTRA_CMDS = [];
const html = readFileSync(pagePath, 'utf8');
const sent = new Set([...html.matchAll(/\b(?:act|call)\('([A-Za-z]+)'/g)].map(m => m[1]));
for (const c of sent) if (!contractCmds.includes(c) && !EXTRA_CMDS.includes(c)) problems.push(`page sends "${c}", which is not in docs/bridge-api.md`);
if (/(?:src|href)\s*=\s*["']https?:|url\(\s*["']?https?:|@import/i.test(html)) problems.push('index.html references an external URL');

// ---- Browser helpers -----------------------------------------------------------------------------------------
// Counts requestAnimationFrame and setInterval calls, so the idle check can prove the page does no work when idle.
const PROBE = `(() => {
  window.__rafCount = 0; window.__intervals = 0;
  const raf = window.requestAnimationFrame.bind(window), si = window.setInterval.bind(window);
  window.requestAnimationFrame = cb => { window.__rafCount++; return raf(cb); };
  window.setInterval = (...a) => { window.__intervals++; return si(...a); };
})();`;
function initScript(opts) {
  return `${PROBE}\nwindow.__MOCK_DATA__ = ${JSON.stringify(data)};\nwindow.__MOCK_OPTS__ = ${JSON.stringify(opts || {})};\n${bridgeSrc}`;
}
async function open(browser, { width = 1180, height = 780, opts = {}, mock = true, ready = true, motion = 'reduce' } = {}) {
  const ctx = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: 1, colorScheme: 'dark', reducedMotion: motion });
  if (mock) await ctx.addInitScript({ content: initScript(opts) });
  const page = await ctx.newPage();
  page.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') problems.push(`[console ${m.type()}] ${m.text()}`); });
  page.on('pageerror', e => problems.push(`[page error] ${e.message}`));
  await page.goto(pageUrl);
  if (mock && ready) await page.waitForSelector('#app:not([hidden])', { timeout: 5000 });
  return page;
}
const settle = (page, ms = 320) => page.waitForTimeout(ms);
async function shot(page, name, ms) {
  await settle(page, ms);
  await page.screenshot({ path: join(outDir, name + '.png') });
  shots.push(name + '.png');
}
async function closeToasts(page) {
  await page.evaluate(() => document.querySelectorAll('.toast').forEach(t => t.remove()));
}
// The page must be completely idle after it settles: no running animation or transition, no animation-frame loop,
// no interval timer (the app pushes state; the page never polls).
async function assertIdle(page, where) {
  await closeToasts(page);
  await settle(page, 400);
  const r = await page.evaluate(async () => {
    const before = window.__rafCount;
    await new Promise(res => setTimeout(res, 1200));
    return {
      raf: window.__rafCount - before,
      anims: document.getAnimations().filter(a => a.playState === 'running').map(a => a.animationName || a.transitionProperty || 'animation'),
      intervals: window.__intervals,
      nodes: document.getElementsByTagName('*').length
    };
  });
  if (r.raf) problems.push(`${where}: ${r.raf} animation frames requested while idle`);
  if (r.anims.length) problems.push(`${where}: animations running while idle (${r.anims.join(', ')})`);
  if (r.intervals) problems.push(`${where}: setInterval was used`);
  if (r.nodes > 6000) problems.push(`${where}: ${r.nodes} DOM elements`);
  idleReport.push(`${where}: idle, ${r.nodes} elements`);
}
async function nav(page, label) {
  await closeToasts(page);
  await page.click(`.nav-item:has-text("${label}")`);
}
const tab = (page, label) => page.click(`.tab:has-text("${label}")`);

// ---- Scenarios -------------------------------------------------------------------------------------------------
const scenarios = [];
const scenario = (name, fn) => scenarios.push({ name, fn });

scenario('main', async browser => {
  const p = await open(browser);
  const cmds = await p.evaluate(() => window.__mockBridge.commands);
  for (const c of contractCmds) if (!cmds.includes(c)) problems.push(`mock-bridge.js does not implement "${c}"`);

  await shot(p, '01-overview', 900);                   // getDeviceInfo takes ~650 ms in the mock

  // Mouse: buttons (top and side views), key capture, picker, profile switcher and menu
  await nav(p, 'Mouse');
  await p.click('[data-fk="mz:TiltL"] > text');
  await shot(p, '03-mouse-buttons');
  await p.hover('[data-fk="mz:M1"] > text');
  await shot(p, '03a-mouse-buttons-hover', 150);
  await p.click('[data-fk="mzv:side"]');
  await p.click('[data-fk="mz:Side8"] > text');
  await shot(p, '03b-mouse-buttons-side');
  await p.click('.capture');
  await p.keyboard.down('Control');
  await shot(p, '04-mouse-buttons-capture', 150);
  await p.keyboard.press('KeyW');
  await p.keyboard.up('Control');
  await settle(p);
  await p.click('[data-fk="mz:Side12"] > text');
  await p.click('.pkey[data-fk="pick:k:DISABLED"]');
  await settle(p);
  await p.click('[data-fk="mz:Side8"] > text');
  await shot(p, '05-mouse-buttons-assigned');
  await p.fill('input[data-fk="pick:q"]', 'num');
  await shot(p, '06-mouse-key-search');
  await p.fill('input[data-fk="pick:q"]', '');
  await closeToasts(p);

  await p.click('.switcher');
  await shot(p, '07-profile-switcher');
  await p.fill('.pop input[type="search"]', 'tarky');
  await shot(p, '08-profile-switcher-search');
  await p.keyboard.press('Enter');                       // picks "Tarky" (turns auto-switch off)
  await settle(p);
  await p.click('.key[data-b="Side4"]');
  await shot(p, '09-mouse-buttons-tarky');
  await closeToasts(p);

  await p.click('button[data-fk="more:mouse"]');
  await shot(p, '10-profile-menu');
  await p.click('.menu-item:has-text("New profile")');
  await p.fill('.modal input.input', 'Tarky');
  await p.click('.modal .btn.primary');
  await shot(p, '11-modal-new-profile');
  await p.keyboard.press('Escape');
  await settle(p);
  await p.click('button[data-fk="more:mouse"]');
  await p.click('.menu-item:has-text("Delete")');
  await shot(p, '12-modal-delete');
  await p.click('.modal .btn.ghost');

  await tab(p, 'Performance');
  await shot(p, '13-mouse-performance');
  await tab(p, 'Lighting');
  await p.click('details.adv summary');
  await p.click('button:has-text("Detect zones")');
  await shot(p, '14-mouse-lighting', 800);
  await tab(p, 'Onboard memory');
  await shot(p, '15-mouse-onboard');
  await p.click('button[data-fk="slotw:4"]');
  await shot(p, '16-modal-onboard-write');
  await p.click('.modal .btn.primary');
  await shot(p, '17-mouse-onboard-writing', 1000);
  await p.waitForSelector('[data-progress="onboard"]', { state: 'detached', timeout: 8000 });
  await settle(p);
  await closeToasts(p);

  // Keyboard
  await nav(p, 'Keyboard');
  await shot(p, '19-keyboard-macro-keys');
  await tab(p, 'Snap Tap');
  await shot(p, '20-keyboard-snap-tap');
  await tab(p, 'Lighting');
  await shot(p, '21-keyboard-lighting');

  // Macros: editor, recording, add key press
  await nav(p, 'Macros');
  await shot(p, '22-macros');
  await p.click('button[data-fk="macro:new"]');
  await p.fill('input[data-fk="macro:name"]', 'Crouch jump');
  await p.click('button[data-fk="macro:rec"]');
  for (const [key, wait] of [['Space', 30], ['ControlLeft', 45], ['Space', 0]]) {
    await p.keyboard.down(key);
    await p.waitForTimeout(wait);
  }
  await p.keyboard.up('Space');
  await p.waitForTimeout(60);
  await p.keyboard.up('ControlLeft');
  await shot(p, '23-macros-recording', 150);
  await p.keyboard.press('Escape');
  await settle(p, 150);
  await p.click('button[data-fk="macro:addkey"]');
  await p.fill('.pop input[type="search"]', 'enter');
  await shot(p, '24-macros-add-key');
  await p.click('.pop .pkey:has-text("Num Enter")');
  await p.click('button[data-fk="macro:save"]');
  await shot(p, '25-macros-saved');

  // Games
  await nav(p, 'Games');
  await shot(p, '26-games');
  await p.click('.grow[data-game="Tarky"] .chip.dashed');
  await shot(p, '27-games-add-program', 600);
  await p.keyboard.press('Escape');
  await p.fill('input[data-fk="games:q"]', 'cod');
  await shot(p, '28-games-search');
  await p.fill('input[data-fk="games:q"]', '');
  await p.selectOption('select[data-fk="link:profile"]', 'cyberpunk');
  await p.click('button[data-fk="link:go"]');
  await shot(p, '29-games-linked', 500);

  // Settings
  await nav(p, 'Settings');
  await shot(p, '30-settings-general');
  await tab(p, 'Gaming');
  await shot(p, '31-settings-gaming');
  await tab(p, 'Performance');
  await shot(p, '32-settings-performance');
  await tab(p, 'Appearance');
  await p.click('.accent-opt:has-text("Violet")');
  await shot(p, '33-settings-appearance-violet');
  await nav(p, 'Overview');
  await shot(p, '34-overview-violet', 900);
  await nav(p, 'Settings');
  await tab(p, 'Appearance');
  await p.click('.accent-opt:has-text("Mint")');
  await tab(p, 'Maintenance');
  await shot(p, '35-settings-maintenance');
  await tab(p, 'Log');
  await shot(p, '36-settings-log', 500);
  await p.click('button[data-fk="exit"]');
  await shot(p, '37a-exit-confirm');
  await p.click('.modal .btn.ghost');
  await p.context().close();
});

scenario('idle', async browser => {
  const p = await open(browser, { motion: 'no-preference' });
  await settle(p, 1200);                                  // device info read finishes
  await assertIdle(p, 'overview');
  for (const [page, tabLabel] of [['Mouse', 'Customize'], ['Mouse', 'Lighting'], ['Keyboard', 'Macro keys'], ['Macros', ''], ['Games', ''], ['Settings', 'Performance']]) {
    await nav(p, page);
    if (tabLabel) await tab(p, tabLabel);
    await assertIdle(p, (page + ' ' + tabLabel).trim().toLowerCase());
  }
  await p.context().close();
});

scenario('alerts', async browser => {
  const p = await open(browser, {
    opts: { state: { elevatedGame: true, inGame: true, liveStatus: '"CS2" is running on the mouse (slot 1, White)', lastGameExe: 'cs2.exe',
      razer: { checked: true, synapseInstalled: true, synapseRunning: true, gmsService: 'disabled', filterDrivers: ['RzDev_008d (Naga)'], rzCommon: true } } }
  });
  await shot(p, '02-overview-in-game-alerts', 900);
  await p.context().close();
});

scenario('errors', async browser => {
  const p = await open(browser, { opts: { fail: ['setPolling'] } });
  await nav(p, 'Mouse');
  await tab(p, 'Performance');
  await p.click('.seg button:has-text("500 Hz")');
  await shot(p, '37-error-toast', 400);
  await p.context().close();
  const q = await open(browser, { opts: { fail: ['getState'] }, ready: false });
  await q.waitForSelector('.boot-t');
  await shot(q, '38-boot-error');
  await q.context().close();
  const r = await open(browser, { mock: false, ready: false });
  await r.waitForSelector('.boot-t');
  await shot(r, '39-open-from-litesynapse');
  await r.context().close();
});

scenario('small', async browser => {
  const p = await open(browser, { width: 1000, height: 700 });
  await shot(p, '40-overview-1000', 900);
  await nav(p, 'Mouse');
  await shot(p, '41-mouse-buttons-1000');
  await tab(p, 'Performance');
  await shot(p, '42-mouse-performance-1000');
  await nav(p, 'Keyboard');
  await shot(p, '43-keyboard-1000');
  await nav(p, 'Macros');
  await shot(p, '44-macros-1000');
  await nav(p, 'Games');
  await shot(p, '45-games-1000');
  await nav(p, 'Settings');
  await tab(p, 'Performance');
  await shot(p, '46-settings-performance-1000');
  await p.context().close();
});

// ---- Run ---------------------------------------------------------------------------------------------------------
mkdirSync(outDir, { recursive: true });
if (!only) for (const f of readdirSync(outDir)) if (f.endsWith('.png')) rmSync(join(outDir, f));
const browser = await chromium.launch();
try {
  for (const s of scenarios) {
    if (only && !s.name.includes(only)) continue;
    try { await s.fn(browser); }
    catch (e) { problems.push(`scenario "${s.name}" failed: ${e.message.split('\n')[0]}`); }
  }
} finally {
  await browser.close();
}
console.log(`${shots.length} screenshots in ${outDir}`);
for (const s of shots) console.log('  ' + s);
for (const s of idleReport) console.log('  ' + s);
if (problems.length) {
  console.error(`\n${problems.length} problem(s):`);
  for (const p of problems) console.error('  - ' + p);
  process.exit(1);
}
console.log('No console errors, page errors, idle work or contract gaps.');
