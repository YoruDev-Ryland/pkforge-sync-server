import { describe, it, expect } from 'vitest';
import { unzipSync, strFromU8 } from 'fflate';
import { exportName, extOf, buildZip } from './download';

const info = (extra = {}) => ({ species: 254, form: 0, shiny: false, nickname: 'Leafy', level: 62, generation: 3, sourceName: 'Emerald', ...extra });

describe('downloads', () => {
  it('names files like the app', () => {
    expect(exportName(info(), '957043EC', 'pk3')).toBe('254 - LEAFY - 957043EC.pk3');
    expect(exportName(info({ species: 25, nickname: 'Pika/Chu?' }), null, 'pk9')).toBe('025 - PIKA_CHU_.pk9');
  });
  it('falls back to the species when there is no nickname', () => {
    expect(exportName(info({ nickname: '' }), null, 'pk3')).toMatch(/^254 - (SCEPTILE|#254)\.pk3$/);
    expect(exportName(info({ nickname: '   ' }), 'AB', 'pk3')).toMatch(/^254 - (SCEPTILE|#254) - AB\.pk3$/);
  });
  it('trims trailing dots and spaces that Windows drops', () => {
    expect(exportName(info({ nickname: 'Mr. ' }), null, 'pk3')).toBe('254 - MR.pk3');
    const files = unzipSync(buildZip([{ folder: '01 Box. ', name: '001 - X.pk3', data: new Uint8Array([1]) }]));
    expect(Object.keys(files)).toEqual(['01 Box/001 - X.pk3']);
  });
  it('derives the extension from format, else generation', () => {
    expect(extOf(info({ format: 'PK4' }))).toBe('pk4');
    expect(extOf(info({ format: 'PB8' }))).toBe('pb8');
    expect(extOf(info({ format: 'Unbound' }))).toBe('pk3');
    expect(extOf(info())).toBe('pk3');
  });
  it('zips per box and lists what was missing', () => {
    const zip = buildZip([
      { folder: '01 Keepers', name: '254 - LEAFY.pk3', data: new Uint8Array([1, 2, 3]) },
      { folder: '02 Box 02', name: '025 - PIKACHU.pk9', data: null },
    ]);
    const files = unzipSync(zip);
    expect(Object.keys(files)).toContain('01 Keepers/254 - LEAFY.pk3');
    expect(strFromU8(files['missing.txt'])).toContain('02 Box 02/025 - PIKACHU.pk9');
  });
  it('keeps same-named files apart within a folder', () => {
    const one = new Uint8Array([1]);
    const files = unzipSync(buildZip([
      { folder: '01 Box 01', name: '025 - PIKACHU.pk9', data: one },
      { folder: '01 Box 01', name: '025 - PIKACHU.pk9', data: one },
      { folder: '01 Box 01', name: '025 - PIKACHU.pk9', data: one },
      { folder: '02 Box 02', name: '025 - PIKACHU.pk9', data: one },
    ]));
    expect(Object.keys(files).sort()).toEqual(['01 Box 01/025 - PIKACHU (2).pk9', '01 Box 01/025 - PIKACHU (3).pk9', '01 Box 01/025 - PIKACHU.pk9', '02 Box 02/025 - PIKACHU.pk9']);
    expect(files['missing.txt']).toBeUndefined();
  });
  it('keeps folder names safe for file systems', () => {
    const files = unzipSync(buildZip([{ folder: '03 A/B: C', name: '001 - X.pk3', data: new Uint8Array([1]) }]));
    expect(Object.keys(files)).toEqual(['03 A_B_ C/001 - X.pk3']);
  });
});
