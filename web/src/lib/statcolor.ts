// statColor/judge, plus the plain stat line the Stats hexagon draws underneath.

/** Base stat bar color, red (low) to teal (high). */
export const statColor = (v: number) => v < 50 ? '#e2583e' : v < 70 ? '#e8973a' : v < 90 ? '#e3c33c' : v < 110 ? '#9ccf3f' : v < 130 ? '#3fbf6a' : '#26b7c9';

/** The games' IV judge wording (Gen 3+ scale, 0–31). */
export const judge = (v: number) => v === 31 ? 'Best' : v >= 30 ? 'Fantastic' : v >= 26 ? 'Very good' : v >= 16 ? 'Pretty good' : v >= 1 ? 'Decent' : 'No good';

/** The species' stats at `level` with no IVs, no EVs and a neutral nature (HP, Atk, Def, SpA, SpD, Spe). */
export const plainStats = (base: number[], level: number) =>
  base.map((b, k) => Math.floor(2 * b * level / 100) + (k === 0 ? level + 10 : 5));

/** Type colors. */
export const TYPE_COLOR: Record<string, string> = { Bug: '#86a32a', Grass: '#3fa451', Fire: '#e0662c', Water: '#3a80d0', Electric: '#d9b21c', Normal: '#a39b84', Psychic: '#dd5585', Ghost: '#7462c2', Dragon: '#5466d2', Poison: '#9a52b9', Ground: '#c09048', Flying: '#8aa2e4', Steel: '#7f93a6', Dark: '#5f5150', Ice: '#5cc4d6', Rock: '#b09848', Fairy: '#e08ad0', Fighting: '#c9513a' };
export const typeColor = (t: string) => TYPE_COLOR[t] ?? '#999';

const luminance = (hex: string) => [1, 3, 5]
  .map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
  .map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4))
  .reduce((a, c, i) => a + c * [0.2126, 0.7152, 0.0722][i], 0);
/** White text is kept (the games' look) unless it falls below 2.6:1 on the type color; those badges get dark ink. */
export const typeDark = (t: string) => 1.05 / (luminance(typeColor(t)) + 0.05) < 2.6;

export const STAT_NAMES = ['HP', 'Attack', 'Defense', 'Sp. Atk', 'Sp. Def', 'Speed'];
export const STAT_SHORT = ['HP', 'Atk', 'Def', 'SpA', 'SpD', 'Spe'];

// Ribbon swatches, picked by the words in PKHeX's ribbon names ("Champion G3", "G3 Cool Super").
const RIBBON_WORDS: [RegExp, string][] = [
  [/Champion|Master/, '#d8a020'], [/Cool/, '#d9473f'], [/Beauty/, '#3a7bd5'], [/Cute/, '#e07ab0'], [/Smart|Clever/, '#3fa451'],
  [/Tough/, '#d9b21c'], [/Effort/, '#e0662c'], [/Winning/, '#7462c2'], [/Victory/, '#5466d2'], [/Artist/, '#a39b84'], [/Country|National|World/, '#3a80d0'],
];
const RIBBON_OTHER = ['#c9513a', '#3fa451', '#5466d2', '#d9b21c', '#9a52b9', '#3a80d0', '#e0662c', '#7f93a6'];
export function ribbonColor(name: string): string {
  for (const [re, c] of RIBBON_WORDS) if (re.test(name)) return c;
  let h = 0;
  for (const ch of name) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
  return RIBBON_OTHER[h % RIBBON_OTHER.length];
}

/** "2026-03-03" → "Mar 3, 2026" (a calendar date, so no time zone shift). */
export function calendarDate(iso: string | null | undefined): string | null {
  const m = iso ? /^(\d{4})-(\d{2})-(\d{2})/.exec(iso) : null;
  if (!m) return null;
  return new Date(+m[1], +m[2] - 1, +m[3]).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
}
