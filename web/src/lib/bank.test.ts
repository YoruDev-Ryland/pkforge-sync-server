import { describe, it, expect } from 'vitest';
import { Bank, boxLabel, type Fetcher } from './bank.svelte';
import { ApiError } from './api';
import type { ChangesResponse, SyncItem } from './protocol';

const mon = (id: string, box: number, slot: number, version = 1): SyncItem => ({
  kind: 'mon', id, version, deleted: false,
  value: { box, slot, movedUtc: '2026-10-15T00:00:00Z', addedUtc: '2026-10-15T00:00:00Z', blob: 'a'.repeat(64),
    info: { species: 25, form: 0, shiny: false, nickname: 'PIKACHU', level: 5, generation: 3, sourceName: 'Emerald' } },
});
const boxes = (count: number, names: (string | null)[] = [], version = 1): SyncItem =>
  ({ kind: 'boxes', id: 'bank', version, deleted: false, value: { count, names, wallpapers: [] } });

function pages(...responses: (ChangesResponse | Error)[]): Fetcher & { calls: number[] } {
  const calls: number[] = [];
  return { calls, async changes(since) { calls.push(since); const r = responses.shift()!; if (r instanceof Error) throw r; return r; } };
}

describe('Bank', () => {
  it('loads every page from 0', async () => {
    const f = pages(
      { items: [boxes(3), mon('a', 0, 0)], next: 2, hasMore: true, epoch: 'e1' },
      { items: [mon('b', 2, 29, 3)], next: 3, hasMore: false, epoch: 'e1' });
    const bank = new Bank(f);
    await bank.load();
    expect(f.calls).toEqual([0, 2]);
    expect(bank.boxCount).toBe(3);
    expect(bank.monsIn(0)[0]?.id).toBe('a');
    expect(bank.monsIn(2)[29]?.id).toBe('b');
    expect(bank.cursor).toBe(3);
    expect(bank.epoch).toBe('e1');
  });

  it('applies newer versions and drops deletions', async () => {
    const bank = new Bank(pages({ items: [mon('a', 0, 0, 1)], next: 1, hasMore: false, epoch: 'e' }));
    await bank.load();
    bank.apply([{ ...mon('a', 0, 5, 1) }]);                  // same version: ignored
    expect(bank.monsIn(0)[0]?.id).toBe('a');
    bank.apply([mon('a', 0, 5, 2)]);
    expect(bank.monsIn(0)[5]?.id).toBe('a');
    bank.apply([{ kind: 'mon', id: 'a', version: 3, deleted: true, value: null }]);
    expect(bank.allMons()).toHaveLength(0);
  });

  it('reloads from scratch on 410', async () => {
    const f = pages(
      { items: [mon('a', 0, 0)], next: 5, hasMore: false, epoch: 'old' },
      new ApiError(410, 'resync_required', 'x'),
      { items: [mon('z', 1, 1)], next: 9, hasMore: false, epoch: 'new' });
    const bank = new Bank(f);
    await bank.load();
    expect(await bank.poll()).toBe(true);
    expect(f.calls).toEqual([0, 5, 0]);
    expect(bank.mon('a')).toBeUndefined();
    expect(bank.mon('z')).toBeDefined();
    expect(bank.epoch).toBe('new');
  });

  it('labels boxes like the app, past the end of short name lists too', () => {
    expect(boxLabel({ count: 12, names: ['Keepers', null, ''] }, 0)).toBe('Keepers');
    expect(boxLabel({ count: 12, names: ['Keepers', null, ''] }, 1)).toBe('Box 02');
    expect(boxLabel({ count: 12, names: ['Keepers', null, ''] }, 2)).toBe('Box 03');
    expect(boxLabel({ count: 12, names: ['Keepers'] }, 11)).toBe('Box 12');
  });

  it('forgets everything on reset, for the next account', async () => {
    const bank = new Bank(pages({ items: [boxes(3), mon('a', 0, 0)], next: 2, hasMore: false, epoch: 'e1' }));
    await bank.load();
    bank.reset();
    expect([bank.loaded, bank.cursor, bank.epoch, bank.items.size, bank.boxCount]).toEqual([false, 0, null, 0, 1]);
  });

  it('defaults to one box before any layout exists', async () => {
    const bank = new Bank(pages({ items: [], next: 0, hasMore: false, epoch: 'e' }));
    await bank.load();
    expect(bank.boxCount).toBe(1);
  });
});
