import { describe, it, expect } from 'vitest';
import { Bank } from './bank.svelte';
import { BankFull, restoreMon, restoreSpot, type View } from './changes';
import type { MonEntry } from './bank.svelte';
import type { MonValue, ReleasedMon, ReleasedResponse, SyncItem } from './protocol';
import { Released, releaseNote } from './released.svelte';
import { Sync } from './sync.svelte';

const NOW = '2026-10-15T12:00:00.000Z';
const BEFORE = '2026-01-01T00:00:00Z';
const info = { species: 25, form: 0, shiny: true, nickname: 'Sparky', level: 12, generation: 9, sourceName: 'Scarlet' };
const value = (box: number, slot: number): MonValue => ({ box, slot, info, blob: 'b'.repeat(64), movedUtc: BEFORE, addedUtc: BEFORE });
const gone = (box: number, slot: number, version = 9): ReleasedMon => ({ id: 'r', version, releasedUtc: NOW, value: value(box, slot) });

/** A Bank of `count` boxes; `full` boxes are full, `taken` lists other occupied [box, slot]s. */
function view(count: number, taken: [number, number][] = [], full: number[] = [], names: (string | null)[] = []): View {
  const entries: MonEntry[] = [];
  for (const b of full) for (let s = 0; s < 30; s++) entries.push({ id: `f${b}-${s}`, version: 1, value: value(b, s) });
  taken.forEach(([b, s], i) => entries.push({ id: `t${i}`, version: 1, value: value(b, s) }));
  return {
    boxes: { count, names }, boxCount: count, settings: {},
    allMons: () => entries, mon: (id) => entries.find((e) => e.id === id),
    monsIn: (b) => { const row: (MonEntry | undefined)[] = new Array(30).fill(undefined); for (const e of entries) if (e.value.box === b) row[e.value.slot] = e; return row; },
    version: () => 0,
  };
}

describe('restoreSpot', () => {
  it('puts it back where it was when that slot is free', () => {
    expect(restoreSpot(view(3), { box: 1, slot: 7 })).toEqual({ box: 1, slot: 7 });
  });

  it('else takes the first free slot of its old box', () => {
    expect(restoreSpot(view(3, [[1, 7], [1, 0]]), { box: 1, slot: 7 })).toEqual({ box: 1, slot: 1 });
  });

  it('else the boxes after it, then wraps around to the first', () => {
    expect(restoreSpot(view(4, [], [1]), { box: 1, slot: 7 })).toEqual({ box: 2, slot: 0 });
    expect(restoreSpot(view(3, [], [1, 2]), { box: 1, slot: 7 })).toEqual({ box: 0, slot: 0 });
  });

  it('starts at the last box when its old box was deleted', () => {
    expect(restoreSpot(view(2), { box: 5, slot: 3 })).toEqual({ box: 1, slot: 0 });
    expect(restoreSpot(view(2, [], [1]), { box: 5, slot: 3 })).toEqual({ box: 0, slot: 0 });
  });

  it('has nowhere to go when every box is full', () => {
    expect(restoreSpot(view(2, [], [0, 1]), { box: 0, slot: 0 })).toBeNull();
  });
});

describe('restoreMon', () => {
  it('pushes the same id on top of its tombstone with its old value and bytes', () => {
    const c = restoreMon(view(3, [], [], ['Keepers']), gone(0, 4), NOW, () => 'g')!;
    expect(c.group).toEqual({ clientGroupId: 'g', writes: [{ kind: 'mon', id: 'r', baseVersion: 9, value: value(0, 4) }] });
    expect(c.apply.get('mon:r')).toEqual(value(0, 4));
    expect(c.label).toBe('Restored Sparky to Keepers');
  });

  it('moves it (and stamps the move) when its slot is taken', () => {
    const c = restoreMon(view(3, [[2, 4]]), gone(2, 4), NOW, () => 'g')!;
    const v = c.group.writes[0].value as MonValue;
    expect([v.box, v.slot, v.movedUtc, v.blob, v.info]).toEqual([2, 0, NOW, 'b'.repeat(64), info]);
    expect(c.label).toBe('Restored Sparky to Box 03');
  });

  it('refuses when the Bank is full and does nothing when it is already back', () => {
    expect(() => restoreMon(view(1, [], [0]), gone(0, 0), NOW)).toThrow(BankFull);
    expect(() => restoreMon(view(1, [], [0]), gone(0, 0), NOW)).toThrow('Every box is full. Make room, then restore.');
    const back = view(1);
    back.mon = (id) => (id === 'r' ? { id, version: 10, value: value(0, 0) } : undefined);
    expect(restoreMon(back, gone(0, 0), NOW)).toBeNull();
  });
});

describe('restoring through Sync', () => {
  const live = (version: number, box = 0, slot = 4): SyncItem => ({ kind: 'mon', id: 'r', version, deleted: false, value: value(box, slot) });
  async function emptyBank() {
    const bank = new Bank({ changes: async () => ({ items: [{ kind: 'boxes', id: 'bank', version: 1, deleted: false, value: { count: 2 } }], next: 1, hasMore: false, epoch: 'e' }) });
    await bank.load();
    return bank;
  }

  it('shows it at once and adopts the server version', async () => {
    const bank = await emptyBank();
    const sync = new Sync(bank, { push: async (r) => ({ version: 10, epoch: 'e', results: [{ clientGroupId: r.groups[0].clientGroupId, status: 'accepted', items: [live(10)] }] }) });
    const pending = sync.run(() => restoreMon(bank, gone(0, 4), NOW));
    expect(bank.monsIn(0)[4]?.id).toBe('r');
    expect(await pending).toEqual({ ok: true });
    expect(bank.version('mon:r')).toBe(10);
  });

  it('takes it away again, with plain words, when the window already ended', async () => {
    const bank = await emptyBank();
    const sync = new Sync(bank, { push: async (r) => ({ version: 9, epoch: 'e', results: [{ clientGroupId: r.groups[0].clientGroupId, status: 'rejected', items: [], error: 'missing_blob', message: 'Blob x was not uploaded.' }] }) });
    expect(await sync.run(() => restoreMon(bank, gone(0, 4), NOW))).toEqual({ ok: false, message: "That Pokémon is no longer on the server, so it can't be restored." });
    expect(bank.mon('r')).toBeUndefined();
  });
});

describe('Released', () => {
  const response = (n: number, retentionDays = 30): ReleasedResponse => ({ retentionDays, items: Array.from({ length: n }, (_, i) => ({ ...gone(0, i), id: `r${i}` })) });

  it('loads the list and the window', async () => {
    const r = new Released(async () => response(2));
    await r.load();
    expect([r.loaded, r.retentionDays, r.items.map((i) => i.id)]).toEqual([true, 30, ['r0', 'r1']]);
  });

  it('fetches once more when asked again mid-flight, so a change during the fetch is not missed', async () => {
    let calls = 0;
    let release!: () => void;
    const r = new Released(() => { calls++; return calls === 1 ? new Promise((ok) => (release = () => ok(response(1)))) : Promise.resolve(response(3)); });
    const first = r.load();
    const second = r.load();
    const third = r.load();
    release();
    await Promise.all([first, second, third]);
    expect(calls).toBe(2);
    expect(r.items).toHaveLength(3);
  });

  it('keeps the old list and says why when a fetch fails', async () => {
    let fail = false;
    const r = new Released(async () => { if (fail) throw new Error('x'); return response(1); });
    await r.load();
    fail = true;
    await r.load();
    expect(r.items).toHaveLength(1);
    expect(r.error).toBe('Something went wrong. Try again.');
  });

  it('forgets everything on sign-out, including an answer still on its way', async () => {
    let release!: () => void;
    const r = new Released(() => new Promise((ok) => (release = () => ok(response(2)))));
    const pending = r.load();
    r.reset();
    release();
    await pending;
    expect([r.loaded, r.retentionDays, r.items]).toEqual([false, null, []]);
  });

  it('asks for the window only once, and gives up waiting quickly', async () => {
    let calls = 0;
    const r = new Released(async () => { calls++; return response(0, 7); });
    expect(await r.windowDays()).toBe(7);
    expect(await r.windowDays()).toBe(7);
    expect(calls).toBe(1);
    const slow = new Released(() => new Promise(() => {}));
    expect(await slow.windowDays(5)).toBeNull();
  });
});

describe('releaseNote', () => {
  it('names the window, or keeps the app-snapshot note when releases are final', () => {
    expect(releaseNote(30, true)).toBe('You can restore it for 30 days from Recently released.');
    expect(releaseNote(1, false)).toBe('You can restore them for 1 day from Recently released.');
    expect(releaseNote(0, true)).toBe('Each app keeps Bank snapshots, so it can still be restored from a phone.');
    expect(releaseNote(null, false)).toBe('Each app keeps Bank snapshots, so they can still be restored from a phone.');
  });
});
