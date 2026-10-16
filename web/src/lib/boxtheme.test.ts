import { describe, it, expect } from 'vitest';
import themes from './fixtures/wallpaper-themes.json';
import { THEMES, nearestTheme, resolveTheme, BOX_THEME } from './theme';
import { ART_GROUPS, PALETTE, themeOfWallpaper, boxTheme } from './wallpapers';
import { shownBox } from './router.svelte';

const IDS = THEMES.map((t) => t[0]) as string[];

describe('nearestTheme', () => {
  it.each([
    ['#b0703c', 'ground'],  // sandy brown desert
    ['#c8a165', 'rock'],    // pale desert tan
    ['#4caf50', 'grass'],   // forest green
    ['#8fd16a', 'grass'],   // light forest green
    ['#1e50b4', 'water'],   // sea blue
    ['#e03a2e', 'fire'],    // red
    ['#f2d02a', 'electric'],
    ['#888888', 'default'], // grey
    ['#f4f4f4', 'default'], // near white
    ['#101010', 'default'], // near black
  ])('%s → %s', (hex, id) => expect(nearestTheme(hex)).toBe(id));
  it('maps its own theme colors to themselves (the greys Normal, Dark and Steel read as Default)', () => {
    for (const [id, , hex] of THEMES) expect(nearestTheme(hex), id).toBe(['normal', 'dark', 'steel'].includes(id) ? 'default' : id);
  });
});

describe('wallpaper themes', () => {
  it('has a theme for every catalog wallpaper, and only valid ones', () => {
    const map = themes as Record<string, string>;
    for (const a of ART_GROUPS.flatMap((g) => g.assets)) expect(IDS, a).toContain(map[a]);
    for (const id of Object.values(map)) expect(IDS).toContain(id);
  });
  it('maps a forest to grass and a volcano to ground', () => {
    expect(themeOfWallpaper('art:box_wp01rs')).toBe('grass');
    expect(themeOfWallpaper('art:box_wp06rs')).toBe('ground');
  });
  it('maps every palette color, the dark navy tints landing on blue-ish themes or Default', () => {
    PALETTE.forEach((_, i) => expect(['default', 'water', 'steel', 'ghost', 'poison', 'flying']).toContain(themeOfWallpaper(`color:${i}`)));
    expect(themeOfWallpaper('color:9')).toBe(themeOfWallpaper('color:1'));
  });
  it('maps unknown and malformed ids to Default', () => {
    for (const bad of ['art:nope', 'art:', 'art:../x', 'color:', 'blue', '']) expect(themeOfWallpaper(bad), bad).toBe('default');
  });
  it('follows the box wallpaper, with the position default when none is chosen', () => {
    expect(boxTheme({ count: 3, wallpapers: [null, 'art:box_wp01rs'] }, 1)).toBe('grass');
    expect(boxTheme({ count: 3 }, 2)).toBe(themeOfWallpaper('color:2'));
  });
});

describe('theme resolution', () => {
  it('uses the shown box only in Match box mode', () => {
    expect(BOX_THEME).toBe('box');
    expect(resolveTheme('box', 'ground')).toBe('ground');
    expect(resolveTheme('box', 'grass')).toBe('grass');
    expect(resolveTheme('fire', 'ground')).toBe('fire');
    expect(resolveTheme('nonsense', 'ground')).toBe('default');
  });
  it('the shown box is the route box on Bank pages and the last viewed box elsewhere', () => {
    expect(shownBox({ page: 'bank', box: 4 }, 1, 10)).toBe(4);
    expect(shownBox({ page: 'bank', box: 40 }, 1, 10)).toBe(9);
    expect(shownBox({ page: 'bank', box: null }, 3, 10)).toBe(3);
    expect(shownBox({ page: 'account' }, 3, 10)).toBe(3);
  });
});
