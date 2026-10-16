// Maps every in-game box wallpaper to the app color theme closest to its dominant color, for the "Match box" theme.
// Run `npm run gen:wallpaper-themes` after the wallpaper catalog or the PKHeX images change; the JSON is checked in.
//
// Method, per wallpaper PNG (external/PKHeX/PKHeX.Drawing.Misc/Resources/img/box/<game>/<asset>.png):
//  1. Every opaque pixel (alpha >= 128) is converted to OKLab. Pixels that carry no color are ignored: near grey
//     (chroma < GREY_CHROMA), near black (L < 0.2) and near white (L > 0.97).
//  2. The rest go into a 24-bin hue histogram (15° each), each pixel weighted by its chroma, so large and colorful
//     areas count most. The histogram is smoothed over neighboring bins and its peak is the dominant hue.
//  3. The dominant color is the chroma-weighted mean (OKLab) of the pixels within ±22.5° of that peak.
//  4. If the colored pixels are too few or too dull (chroma-weighted coverage below MIN_COVERAGE of all opaque
//     pixels), the wallpaper has no clear color and maps to Default.
//  5. Otherwise the theme is nearestThemeLab() from src/lib/theme.ts — the same function the web uses for the
//     flat palette colors: smallest OKLab distance to each theme's hex, chroma lifted, lightness weighted down.
// Catalog assets whose PNG is missing map to Default and are listed on stderr.
import { readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { PNG } from 'pngjs';
import { GREY_CHROMA, nearestThemeLab, oklab } from '../src/lib/theme.ts';

const here = (p) => fileURLToPath(new URL(p, import.meta.url));
const ROOT = here('../../external/PKHeX/PKHeX.Drawing.Misc/Resources/img/box/');
const OUT = here('../src/lib/fixtures/wallpaper-themes.json');
const catalog = JSON.parse(readFileSync(here('../src/lib/fixtures/wallpapers.json'), 'utf8'));
const verbose = process.argv.includes('--verbose');

const BINS = 24, MIN_COVERAGE = 0.02;

const files = new Map();
for (const game of readdirSync(ROOT, { withFileTypes: true })) {
  if (!game.isDirectory()) continue;
  for (const f of readdirSync(ROOT + game.name)) if (f.endsWith('.png')) files.set(f.slice(0, -4), `${ROOT}${game.name}/${f}`);
}

const hex2 = (n) => n.toString(16).padStart(2, '0');

function dominant(path) {
  const png = PNG.sync.read(readFileSync(path));
  const px = [], hist = new Float64Array(BINS);
  let opaque = 0, weight = 0;
  const cache = new Map();
  for (let i = 0; i < png.data.length; i += 4) {
    if (png.data[i + 3] < 128) continue;
    opaque++;
    const hex = '#' + hex2(png.data[i]) + hex2(png.data[i + 1]) + hex2(png.data[i + 2]);
    let lab = cache.get(hex);
    if (!lab) cache.set(hex, (lab = oklab(hex)));
    const [L, a, b] = lab, c = Math.hypot(a, b);
    if (c < GREY_CHROMA || L < 0.2 || L > 0.97) continue;
    const h = (Math.atan2(b, a) * 180 / Math.PI + 360) % 360;
    hist[Math.floor(h / (360 / BINS)) % BINS] += c;
    weight += c;
    px.push([L, a, b, c, h]);
  }
  const coverage = opaque ? weight / opaque : 0;
  if (coverage < MIN_COVERAGE) return { coverage };
  let peak = 0, peakW = -1;
  for (let i = 0; i < BINS; i++) {
    const w = hist[(i + BINS - 1) % BINS] + hist[i] + hist[(i + 1) % BINS];
    if (w > peakW) { peak = i; peakW = w; }
  }
  const center = (peak + 0.5) * (360 / BINS);
  let sL = 0, sa = 0, sb = 0, sw = 0;
  for (const [L, a, b, c, h] of px) {
    const d = Math.abs(((h - center + 540) % 360) - 180);
    if (d > 22.5) continue;
    sL += L * c; sa += a * c; sb += b * c; sw += c;
  }
  return { coverage, lab: [sL / sw, sa / sw, sb / sw] };
}

const out = {}, missing = [];
for (const asset of catalog.groups.flatMap((g) => g.assets)) {
  const path = files.get(asset);
  if (!path) { missing.push(asset); out[asset] = 'default'; continue; }
  const d = dominant(path);
  out[asset] = d.lab ? nearestThemeLab(d.lab) : 'default';
  if (verbose) {
    const lab = d.lab ? `L ${d.lab[0].toFixed(2)} C ${Math.hypot(d.lab[1], d.lab[2]).toFixed(3)} h ${((Math.atan2(d.lab[2], d.lab[1]) * 180 / Math.PI + 360) % 360).toFixed(0)}` : 'no clear color';
    console.log(`${asset.padEnd(14)} ${out[asset].padEnd(9)} coverage ${d.coverage.toFixed(3)}  ${lab}`);
  }
}
if (missing.length) console.error(`No PNG for ${missing.length} catalog assets (mapped to default): ${missing.join(', ')}`);
writeFileSync(OUT, JSON.stringify(out, null, 2) + '\n');
console.log(`Wrote ${Object.keys(out).length} wallpaper themes to ${OUT}`);
