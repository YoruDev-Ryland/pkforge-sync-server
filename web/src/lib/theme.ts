export type Mode = 'system' | 'light' | 'dark';

/** The app's color themes (ColorThemes.g.cs, "Blue" role), in its order. */
export const THEMES = [
  ['default', 'Default', '#2789cd'], ['normal', 'Normal', '#b7b4b7'], ['fighting', 'Fighting', '#ecac47'],
  ['flying', 'Flying', '#8c6fd9'], ['poison', 'Poison', '#9440ac'], ['ground', 'Ground', '#ab6531'],
  ['rock', 'Rock', '#c18e44'], ['bug', 'Bug', '#898f11'], ['ghost', 'Ghost', '#5a34a8'], ['steel', 'Steel', '#586d7a'],
  ['fire', 'Fire', '#dc182a'], ['water', 'Water', '#0057be'], ['grass', 'Grass', '#00ae00'], ['electric', 'Electric', '#dfc900'],
  ['psychic', 'Psychic', '#c653bf'], ['ice', 'Ice', '#00afb3'], ['dragon', 'Dragon', '#958017'], ['dark', 'Dark', '#404040'],
  ['fairy', 'Fairy', '#f8a9f0'],
] as const satisfies readonly (readonly [string, string, string])[];

/** Mirrors the CSS accent mix (12% toward white in dark mode, 14% toward black in light) and picks readable ink. */
export function inkFor(hex: string, dark: boolean): '#141414' | '#ffffff' {
  const rgb = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255);
  const mixed = rgb.map((c) => (dark ? c + (1 - c) * 0.12 : c * 0.86));
  const lum = mixed.map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4)).reduce((a, c, i) => a + c * [0.2126, 0.7152, 0.0722][i], 0);
  return lum > 0.36 ? '#141414' : '#ffffff';
}

/** The "Match box" choice: the theme follows the wallpaper of the box being shown. */
export const BOX_THEME = 'box';

/** The theme to show: a fixed choice as is, Match box as the shown box's theme; anything unknown as Default. */
export function resolveTheme(choice: string, boxTheme: ThemeId): ThemeId {
  if (choice === BOX_THEME) return boxTheme;
  return THEMES.find((t) => t[0] === choice)?.[0] ?? 'default';
}

export function hueOf(themeId: string): string {
  return (THEMES.find((t) => t[0] === themeId) ?? THEMES[0])[2];
}

/** Sets the colors on <html> and caches them (with the choice behind them) for index.html's first paint. */
export function applyTheme(root: HTMLElement, mode: Mode, themeId: string, systemDark: boolean, choice: string = themeId): void {
  const hue = hueOf(themeId);
  const dark = mode === 'dark' || (mode === 'system' && systemDark);
  const ink = inkFor(hue, dark);
  if (mode === 'system') delete root.dataset.mode; else root.dataset.mode = mode;
  root.style.setProperty('--hue', hue);
  root.style.setProperty('--accent-ink', ink);
  try { localStorage.setItem('pkf-theme', JSON.stringify({ mode: mode === 'system' ? '' : mode, hue, ink, theme: choice })); } catch { /* private mode */ }
}

export type ThemeId = (typeof THEMES)[number][0];

/** sRGB hex → OKLab [L, a, b] (Björn Ottosson's matrices). */
export function oklab(hex: string): [number, number, number] {
  const [r, g, b] = [1, 3, 5].map((i) => {
    const c = parseInt(hex.slice(i, i + 2), 16) / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  const l = Math.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
  const m = Math.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
  const s = Math.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
  return [
    0.2104542553 * l + 0.793617785 * m - 0.0040720468 * s,
    1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s,
    0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s,
  ];
}

/** Below this OKLab chroma a color reads as grey: no theme fits it better than Default. */
export const GREY_CHROMA = 0.04;
/** Colors are compared at least this colorful, so a muted wallpaper still lands on the theme of its hue. */
const MIN_CHROMA = 0.1;
/** Hue counts fully, chroma 30% and lightness 25%: the theme hue is mixed into light and dark surfaces
 *  anyway, so a wallpaper should land on the theme of its hue rather than on one that is merely as pale or as dark. */
const L_WEIGHT = 0.25, C_WEIGHT = 0.3;
/** Normal and Dark are greys themselves; a colorful wallpaper never maps to them. */
const THEME_LCH = THEMES.filter(([id]) => id !== 'normal' && id !== 'dark').map(([id, , hex]) => {
  const [L, a, b] = oklab(hex);
  return [id, L, Math.hypot(a, b), Math.atan2(b, a)] as const;
});

/** The app theme closest to a color (OKLab ΔE, chroma lifted, lightness weighted down); greys get Default. */
export function nearestTheme(hex: string): ThemeId {
  return nearestThemeLab(oklab(hex));
}

/** Same, from OKLab: the distance splits into lightness, chroma and hue (ΔH = 2√(C₁C₂)·sin(Δh/2)) like CIE94. */
export function nearestThemeLab([L, a, b]: readonly [number, number, number]): ThemeId {
  const c0 = Math.hypot(a, b);
  if (c0 < GREY_CHROMA) return 'default';
  const c = Math.max(c0, MIN_CHROMA), h = Math.atan2(b, a);
  let best: ThemeId = 'default', bestD = Infinity;
  for (const [id, tl, tc, th] of THEME_LCH) {
    const dH = 2 * Math.sqrt(c * tc) * Math.sin((h - th) / 2);
    const d = Math.hypot((L - tl) * L_WEIGHT, (c - tc) * C_WEIGHT, dH);
    if (d < bestD) { best = id; bestD = d; }
  }
  return best;
}
