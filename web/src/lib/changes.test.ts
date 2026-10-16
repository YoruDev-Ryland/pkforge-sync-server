import { describe, it, expect } from 'vitest';
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { moveMon, sendToBox, moveMany, release, renameBox, setWallpaper, reorderBox, addBox, deleteBox, NoRoom, type View } from './changes';
import type { BoxesValue, MonValue } from './protocol';
import type { MonEntry } from './bank.svelte';

const NOW = '2026-10-15T12:00:00.000Z';
let n = 0; const gid = () => `00000000-0000-4000-8000-${String(++n).padStart(12, '0')}`;
const info = { species: 328, form: 0, shiny: false, nickname: 'TRAPINCH', level: 1, generation: 3, sourceName: 'Emerald' };
function view(mons: [string, number, number][], boxes: BoxesValue = { count: 3, names: [], wallpapers: [] }, settings = {}): View {
  const entries: MonEntry[] = mons.map(([id, box, slot]) => ({ id, version: 7, value: { box, slot, info, blob: 'b'.repeat(64), movedUtc: '2026-01-01T00:00:00Z', addedUtc: '2026-01-01T00:00:00Z' } }));
  return {
    boxes, boxCount: boxes.count, settings,
    allMons: () => entries, mon: (id) => entries.find((e) => e.id === id),
    monsIn: (b) => { const row: (MonEntry | undefined)[] = new Array(30).fill(undefined); for (const e of entries) if (e.value.box === b) row[e.value.slot] = e; return row; },
    version: (k) => (k === 'boxes:bank' ? 4 : k === 'settings:bank' ? 2 : entries.find((e) => `mon:${e.id}` === k)?.version ?? 0),
  };
}
const write = (c: ReturnType<typeof moveMon>, id: string) => c!.group.writes.find((w) => w.id === id)!;

describe('moves', () => {
  it('moves to an empty slot, touching only box, slot and movedUtc', () => {
    const c = moveMon(view([['a', 0, 0]]), 'a', 1, 4, NOW, gid)!;
    const w = write(c, 'a');
    expect(w.baseVersion).toBe(7);
    const v = w.value as MonValue;
    expect([v.box, v.slot, v.movedUtc]).toEqual([1, 4, NOW]);
    expect(v.info).toEqual(info);
    expect(v.blob).toBe('b'.repeat(64));
    expect(v.addedUtc).toBe('2026-01-01T00:00:00Z');
  });
  it('swaps with an occupant in one group', () => {
    const c = moveMon(view([['a', 0, 0], ['b', 0, 1]]), 'a', 0, 1, NOW, gid)!;
    expect(c.group.writes).toHaveLength(2);
    expect((write(c, 'b').value as MonValue).slot).toBe(0);
  });
  it('does nothing when dropped on its own slot', () => {
    expect(moveMon(view([['a', 0, 0]]), 'a', 0, 0, NOW, gid)).toBeNull();
  });
  it('sends to the first free slot of another box', () => {
    const c = sendToBox(view([['a', 0, 0], ['x', 1, 0], ['y', 1, 1]]), 'a', 1, NOW, gid)!;
    expect((write(c, 'a').value as MonValue).slot).toBe(2);
  });
  it('refuses a full box', () => {
    const full: [string, number, number][] = Array.from({ length: 30 }, (_, i) => [`f${i}`, 1, i]);
    expect(() => sendToBox(view([['a', 0, 0], ...full]), 'a', 1, NOW, gid)).toThrow(NoRoom);
  });
  it('moves several in slot order into free slots', () => {
    const c = moveMany(view([['a', 0, 3], ['b', 0, 1], ['x', 2, 0]]), ['a', 'b'], 2, NOW, gid)!;
    expect((write(c, 'b').value as MonValue).slot).toBe(1);
    expect((write(c, 'a').value as MonValue).slot).toBe(2);
  });
  it('moving several into their own box is a no-op', () => {
    expect(moveMany(view([['a', 0, 3]]), ['a'], 0, NOW, gid)).toBeNull();
  });
  it('releases with deletes at the held versions', () => {
    const c = release(view([['a', 0, 0], ['b', 0, 1]]), ['a', 'b'], gid)!;
    expect(c.group.writes.every((w) => w.deleted && w.value === null && w.baseVersion === 7)).toBe(true);
  });
});

describe('box layout', () => {
  it('renames, padding short name lists without disturbing other boxes', () => {
    const c = renameBox(view([], { count: 5, names: ['Keepers'], wallpapers: ['color:2'] }), 3, '  Shiny hunts in progress  ', gid)!;
    const v = c.group.writes[0].value as BoxesValue;
    expect(v.names).toEqual(['Keepers', null, null, 'Shiny hunts in progress']);
    expect(v.wallpapers).toEqual(['color:2']);
    expect(c.group.writes[0].baseVersion).toBe(4);
  });
  it('caps names at 24 and treats blank as the default', () => {
    const v1 = renameBox(view([]), 0, 'x'.repeat(30), gid)!.group.writes[0].value as BoxesValue;
    expect(v1.names![0]).toHaveLength(24);
    const v2 = renameBox(view([], { count: 3, names: ['Old'] }), 0, '   ', gid)!.group.writes[0].value as BoxesValue;
    expect(v2.names![0]).toBeNull();
  });
  it('sets a wallpaper', () => {
    const v = setWallpaper(view([]), 2, 'art:box_wp01e', gid)!.group.writes[0].value as BoxesValue;
    expect(v.wallpapers).toEqual([null, null, 'art:box_wp01e']);
  });
  it('reorders boxes, carrying names, wallpapers, Pokémon and the living dex start', () => {
    const c = reorderBox(view([['a', 0, 0], ['b', 2, 5]], { count: 3, names: ['A', 'B', 'C'], wallpapers: ['color:1', null, 'art:box_wp01e'] }, { livingDexStartBox: 2 }), 2, 0, NOW, gid)!;
    const boxes = c.group.writes.find((w) => w.kind === 'boxes')!.value as BoxesValue;
    expect(boxes.names).toEqual(['C', 'A', 'B']);
    expect(boxes.wallpapers).toEqual(['art:box_wp01e', 'color:1', null]);
    expect((write(c, 'a').value as MonValue).box).toBe(1);
    expect((write(c, 'b').value as MonValue).box).toBe(0);
    const settings = c.group.writes.find((w) => w.kind === 'settings')!;
    expect((settings.value as { livingDexStartBox: number }).livingDexStartBox).toBe(0);
  });
  it('keeps unknown settings keys when it rewrites settings', () => {
    const c = reorderBox(view([], { count: 2, names: [] }, { livingDexStartBox: 1, other: 'x' }), 1, 0, NOW, gid)!;
    expect(c.group.writes.find((w) => w.kind === 'settings')!.value).toEqual({ livingDexStartBox: 0, other: 'x' });
  });
  it('adds a box', () => {
    expect((addBox(view([]), gid).group.writes[0].value as BoxesValue).count).toBe(4);
  });
  it('deletes only empty boxes and shifts later ones down', () => {
    expect(deleteBox(view([['a', 1, 0]]), 1, NOW, gid)).toBeNull();
    const c = deleteBox(view([['a', 2, 0]], { count: 3, names: ['A', 'B', 'C'] }, { livingDexStartBox: 1 }), 1, NOW, gid)!;
    expect((c.group.writes.find((w) => w.kind === 'boxes')!.value as BoxesValue)).toMatchObject({ count: 2, names: ['A', 'C'] });
    expect((write(c, 'a').value as MonValue).box).toBe(1);
    expect((c.group.writes.find((w) => w.kind === 'settings')!.value as { livingDexStartBox: number | null }).livingDexStartBox).toBeNull();
  });
  it('never deletes the last box', () => {
    expect(deleteBox(view([], { count: 1 }), 0, NOW, gid)).toBeNull();
  });
});

describe('web-groups.json (checked by PKForge.Sync.Tests)', () => {
  it('is current', () => {
    n = 1000;
    const start = view([['a', 0, 0], ['b', 0, 1], ['c', 2, 5]], { count: 4, names: ['Keepers', null, 'Trades'], wallpapers: ['color:3', null, 'art:box_wp01e'] }, { livingDexStartBox: 2 });
    const scenarios = {
      start: { mons: [['a', 0, 0], ['b', 0, 1], ['c', 2, 5]], boxes: start.boxes, settings: start.settings },
      steps: [
        moveMon(start, 'a', 1, 4, NOW, gid)!.group,
        moveMon(start, 'a', 0, 1, NOW, gid)!.group,
        moveMany(start, ['a', 'b'], 3, NOW, gid)!.group,
        renameBox(start, 1, 'Shiny hunts in progress', gid)!.group,
        setWallpaper(start, 3, 'art:box_wp20sv_n', gid)!.group,
        reorderBox(start, 2, 0, NOW, gid)!.group,
        release(start, ['b'], gid)!.group,
      ],
    };
    const path = join(dirname(new URL(import.meta.url).pathname), 'fixtures/web-groups.json');
    const text = JSON.stringify(scenarios, null, 2) + '\n';
    if (process.env.UPDATE_FIXTURES) writeFileSync(path, text);
    expect(readFileSync(path, 'utf8')).toBe(text);
  });
});
