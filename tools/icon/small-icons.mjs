// Renders the small app/tray icon frames (16..64 px) of the round logo as PNGs.
//
//   node tools/icon/small-icons.mjs <outDir>
//
// The logo (tools/icon/logo-original.png) has thin rings and "RzR optimizer" text that disappear below 128 px, so
// the small frames are drawn as vectors in the same style: dark disc, green rings with nodes, the rounded square with
// a check mark, with fewer details the smaller the icon gets. make-icon.py calls this; it uses the Playwright copy
// that tools/ui-preview installs.
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const require = createRequire(join(here, '..', 'ui-preview', 'package.json'));
const { chromium } = require('playwright');

export const GREEN = '#70E060';
export const DISC = '#212A2E';
export const SIZES = [16, 20, 24, 32, 40, 48, 64];

const f = v => Number(v.toFixed(2));

// Same proportions as the original (in units of the disc radius R): rings at 0.70, 0.78 and 0.89, the outer ring
// broken at the upper left between two nodes, the square 0.69 wide.
export function logoSvg(S) {
  const c = S / 2, R = S / 2 - (S <= 24 ? 0.25 : 0.5);
  const out = [`<circle cx="${c}" cy="${c}" r="${f(R)}" fill="${DISC}"/>`];
  const pt = (k, deg) => [f(c + k * R * Math.cos(deg * Math.PI / 180)), f(c + k * R * Math.sin(deg * Math.PI / 180))];
  const ring = (k, w) => out.push(`<circle cx="${c}" cy="${c}" r="${f(k * R)}" fill="none" stroke="${GREEN}" stroke-width="${w}"/>`);
  const arc = (k, from, to, w) => {
    const [x1, y1] = pt(k, from), [x2, y2] = pt(k, to);
    out.push(`<path d="M${x1} ${y1}A${f(k * R)} ${f(k * R)} 0 1 1 ${x2} ${y2}" fill="none" stroke="${GREEN}" stroke-width="${w}"/>`);
  };
  const node = (k, deg, r, w) => {
    const [x, y] = pt(k, deg);
    out.push(`<circle cx="${x}" cy="${y}" r="${r}" fill="${DISC}" stroke="${GREEN}" stroke-width="${w}"/>`);
  };
  const square = (side, rx, w) => out.push(`<rect x="${f(c - side / 2)}" y="${f(c - side / 2)}" width="${f(side)}" height="${f(side)}" rx="${f(rx)}" fill="none" stroke="${GREEN}" stroke-width="${w}"/>`);
  const check = (scale, w) => {
    const p = [[-0.17, 0.01], [-0.05, 0.13], [0.18, -0.11]].map(([x, y]) => `${f(c + x * R * scale)} ${f(c + y * R * scale)}`);
    out.push(`<path d="M${p[0]}L${p[1]}L${p[2]}" fill="none" stroke="${GREEN}" stroke-width="${w}" stroke-linecap="round" stroke-linejoin="round"/>`);
  };
  if (S <= 16) {
    ring(0.8, 1.2);
    check(1.6, 1.7);
  } else if (S <= 24) {
    const w = S <= 20 ? 1.2 : 1.35;
    ring(0.84, w);
    square(0.82 * R, 0.16 * R, w);
    check(1.05, w + 0.2);
  } else if (S <= 32) {
    ring(0.89, 1.2);
    ring(0.7, 1.2);
    square(0.7 * R, 0.13 * R, 1.4);
    check(1.0, 1.7);
  } else {
    const w = S >= 64 ? 1.5 : 1.2, nr = S >= 64 ? 2.3 : 1.7;
    arc(0.89, -113, -122, w);
    ring(0.78, w);
    ring(0.7, w);
    for (const deg of [180, 0, -113, -122]) node(0.89, deg, nr, w);
    for (const deg of [180, 0, -90, 90]) node(0.7, deg, nr, w);
    square(0.69 * R, 0.11 * R, S >= 64 ? 1.9 : 1.5);
    check(1.05, S >= 64 ? 2.3 : 1.8);
  }
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${S}" height="${S}" viewBox="0 0 ${S} ${S}">${out.join('')}</svg>`;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const outDir = resolve(process.argv[2] || join(here, 'out'));
  mkdirSync(outDir, { recursive: true });
  const browser = await chromium.launch();
  try {
    const page = await browser.newPage({ deviceScaleFactor: 1 });
    for (const S of SIZES) {
      await page.setViewportSize({ width: S, height: S });
      await page.setContent(`<!doctype html><style>html,body{margin:0;background:transparent}svg{display:block}</style>${logoSvg(S)}`);
      await page.screenshot({ path: join(outDir, `icon-${S}.png`), omitBackground: true, clip: { x: 0, y: 0, width: S, height: S } });
    }
  } finally {
    await browser.close();
  }
  console.log(`${SIZES.length} small frames in ${outDir}`);
}
