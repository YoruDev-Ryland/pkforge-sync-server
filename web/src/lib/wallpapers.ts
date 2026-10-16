import data from './fixtures/wallpapers.json';
import themes from './fixtures/wallpaper-themes.json';
import type { BoxesValue } from './protocol';
import { nearestTheme, THEMES, type ThemeId } from './theme';

// The JSON is generated from the app (PKForge WebParityTests), so the web has one copy of the catalog.
export const PALETTE: string[] = data.palette;
export const ART_GROUPS: { title: string; assets: string[] }[] = data.groups;

const wrap = (i: number) => ((i % PALETTE.length) + PALETTE.length) % PALETTE.length;

export const defaultWallpaper = (box: number) => `color:${wrap(box)}`;

const ART = /^art:[A-Za-z0-9_]{1,64}$/, COLOR = /^color:\d{1,3}$/;

/** A wallpaper id as the server accepts it; the art name ends up in a CSS url(), so nothing else gets through. */
export const validWallpaper = (id: unknown): id is string => typeof id === 'string' && (ART.test(id) || COLOR.test(id));

export function wallpaperOf(boxes: BoxesValue, box: number): { id: string; color?: string; url?: string; tone: string } {
  const chosen = boxes.wallpapers?.[box];
  const id = validWallpaper(chosen) ? chosen : defaultWallpaper(box);
  if (id.startsWith('art:')) return { id, url: `/pkhex/wallpapers/${id.slice(4)}.png`, tone: PALETTE[0] };
  const color = PALETTE[wrap(Number(id.slice(6)))];
  return { id, color, tone: color };
}

const ART_THEMES = themes as Record<string, string>;

/** The app theme closest to a wallpaper's color: art from scripts/wallpaper-themes.mjs, palette colors computed the same way. */
export function themeOfWallpaper(id: string): ThemeId {
  if (!validWallpaper(id)) return 'default';
  if (id.startsWith('color:')) return nearestTheme(PALETTE[wrap(Number(id.slice(6)))]);
  const t = ART_THEMES[id.slice(4)];
  return THEMES.find((x) => x[0] === t)?.[0] ?? 'default';
}

/** The theme of a box, for "Match box". */
export const boxTheme = (boxes: BoxesValue, box: number): ThemeId => themeOfWallpaper(wallpaperOf(boxes, box).id);
