import { describe, it, expect } from 'vitest';
import data from './fixtures/wallpapers.json';
import { PALETTE, ART_GROUPS, wallpaperOf, defaultWallpaper } from './wallpapers';

describe('wallpapers', () => {
  it('uses the app palette and catalog', () => {
    expect(PALETTE).toEqual(data.palette);
    expect(ART_GROUPS).toEqual(data.groups);
  });
  it('defaults by box position like the app', () => {
    expect(defaultWallpaper(0)).toBe('color:0');
    expect(defaultWallpaper(9)).toBe('color:1');
    expect(wallpaperOf({ count: 12 }, 9)).toMatchObject({ id: 'color:1', color: data.palette[1] });
  });
  it('resolves chosen art and colors', () => {
    expect(wallpaperOf({ count: 2, wallpapers: ['art:box_wp01e'] }, 0)).toMatchObject({ url: '/pkhex/wallpapers/box_wp01e.png' });
    expect(wallpaperOf({ count: 2, wallpapers: [null, 'color:5'] }, 1).color).toBe(data.palette[5]);
    expect(wallpaperOf({ count: 2, wallpapers: ['color:13'] }, 0).color).toBe(data.palette[5]);
  });
  it('falls back to the default for ids that are not well formed (they end up in a CSS url())', () => {
    for (const bad of ['art:', 'art:a b', 'art:x")', 'art:../secret', `art:${'a'.repeat(65)}`, 'color:', 'color:1000', 'color:-1', 'color:1.5', 'color: 2', 'blue', '']) {
      expect(wallpaperOf({ count: 4, wallpapers: [null, null, null, bad] }, 3), bad).toEqual(wallpaperOf({ count: 4 }, 3));
    }
    expect(wallpaperOf({ count: 1, wallpapers: [`art:${'a'.repeat(64)}`] }, 0).url).toBe(`/pkhex/wallpapers/${'a'.repeat(64)}.png`);
    expect(wallpaperOf({ count: 1, wallpapers: ['color:999'] }, 0).id).toBe('color:999');
  });
  it('ignores values that are not strings', () => {
    expect(wallpaperOf({ count: 1, wallpapers: [42 as unknown as string] }, 0)).toEqual(wallpaperOf({ count: 1 }, 0));
  });
});
