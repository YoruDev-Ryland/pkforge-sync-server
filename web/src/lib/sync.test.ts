import { describe, it, expect, vi } from 'vitest';
import { Bank } from './bank.svelte';
import { Sync } from './sync.svelte';
import { moveMon, moveMany, release, sendToBox, uuid } from './changes';
import { ApiError } from './api';
import { Connectivity } from './connectivity.svelte';
import type { PushRequest, PushResponse, SyncItem, MonValue } from './protocol';

const NOW = '2026-10-15T12:00:00.000Z';
const mk = (id: string, box: number, slot: number, version = 1): SyncItem => ({ kind: 'mon', id, version, deleted: false,
  value: { box, slot, movedUtc: 'x', addedUtc: 'x', blob: 'b'.repeat(64), info: { species: 1, form: 0, shiny: false, nickname: 'B', level: 1, generation: 3, sourceName: 'E' } } });
async function bankWith(...items: SyncItem[]) {
  const bank = new Bank({ changes: async () => ({ items, next: 1, hasMore: false, epoch: 'e' }) });
  await bank.load();
  return bank;
}
const instant = { delay: async () => {} };

describe('Sync', () => {
  it('applies optimistically and adopts the server versions', async () => {
    const bank = await bankWith(mk('a', 0, 0));
    let sent: PushRequest | undefined;
    const sync = new Sync(bank, { push: async (r) => { sent = r; return { version: 2, epoch: 'e', results: [{ clientGroupId: r.groups[0].clientGroupId, status: 'accepted', items: [mk('a', 0, 5, 2)] }] }; } }, instant);
    const pending = sync.run(() => moveMon(bank, 'a', 0, 5, NOW));
    expect(bank.monsIn(0)[5]?.id).toBe('a');          // before the server answers
    expect(await pending).toEqual({ ok: true });
    expect(sent!.since).toBe(1);
    expect(sent!.epoch).toBe('e');
    expect(bank.version('mon:a')).toBe(2);
  });

  it('rolls back a conflict and says why', async () => {
    const bank = await bankWith(mk('a', 0, 0));
    const sync = new Sync(bank, { push: async (r) => ({ version: 3, epoch: 'e', results: [{ clientGroupId: r.groups[0].clientGroupId, status: 'conflict', items: [mk('a', 1, 1, 3)] }] }) }, instant);
    const result = await sync.run(() => moveMon(bank, 'a', 0, 5, NOW));
    expect(result).toEqual({ ok: false, message: 'That box changed on another device. Try again.' });
    expect(bank.monsIn(1)[1]?.id).toBe('a');          // the server's copy, applied
    expect(bank.monsIn(0)[5]).toBeUndefined();
  });

  it('retries network failures with the same group id, then rolls back', async () => {
    const bank = await bankWith(mk('a', 0, 0));
    const ids: string[] = [];
    const connectivity = new Connectivity(() => false);
    const sync = new Sync(bank, { push: async (r) => { ids.push(r.groups[0].clientGroupId); throw new ApiError(0, 'offline', 'x'); } }, { retries: 3, connectivity, ...instant });
    const result = await sync.run(() => moveMon(bank, 'a', 0, 5, NOW));
    expect(ids).toHaveLength(4);
    expect(new Set(ids).size).toBe(1);
    expect(result).toEqual({ ok: false, message: "Couldn't save that change. Try again." });
    expect(bank.monsIn(0)[0]?.id).toBe('a');
    expect(connectivity.offline).toBe(true);
  });

  it('a push that fails once and then goes through never shows offline', async () => {
    const bank = await bankWith(mk('a', 0, 0));
    const connectivity = new Connectivity(() => false);
    let tries = 0;
    const sync = new Sync(bank, { push: async (r) => {
      if (tries++ === 0) throw new ApiError(0, 'offline', 'x');
      return { version: 2, epoch: 'e', results: [{ clientGroupId: r.groups[0].clientGroupId, status: 'accepted', items: [mk('a', 0, 5, 2)] }] };
    } }, { connectivity, ...instant });
    expect(await sync.run(() => moveMon(bank, 'a', 0, 5, NOW))).toEqual({ ok: true });
    expect(connectivity.offline).toBe(false);
    connectivity.failed(); // the retry's success cleared the suspicion too
    expect(connectivity.offline).toBe(false);
  });

  it('goes read-only on 403 writes_disabled, with the server reason', async () => {
    const bank = await bankWith(mk('a', 0, 0));
    let pushes = 0;
    const sync = new Sync(bank, { push: async () => { pushes++; throw new ApiError(403, 'writes_disabled', 'Storage quota reached.'); } }, instant);
    const result = await sync.run(() => moveMon(bank, 'a', 0, 5, NOW));
    const message = "This server isn't accepting changes for your account: Storage quota reached.";
    expect(result).toEqual({ ok: false, message });
    expect(sync.readOnly).toBe(true);
    expect(sync.readOnlyMessage).toBe(message);
    expect(bank.monsIn(0)[0]?.id).toBe('a');
    // Further changes stop at once, without asking the server again.
    expect(await sync.run(() => moveMon(bank, 'a', 0, 6, NOW))).toEqual({ ok: false, message });
    expect(pushes).toBe(1);
  });

  it('tries again after allowRetry and leaves read-only once a push is accepted', async () => {
    const bank = await bankWith(mk('a', 0, 0));
    let refuse = true;
    const sync = new Sync(bank, { push: async (r) => {
      if (refuse) throw new ApiError(403, 'writes_disabled', "This server isn't accepting changes for this account.");
      return { version: 2, epoch: 'e', results: [{ clientGroupId: r.groups[0].clientGroupId, status: 'accepted', items: [mk('a', 0, 5, 2)] }] };
    } }, instant);
    expect(await sync.run(() => moveMon(bank, 'a', 0, 5, NOW))).toEqual({ ok: false, message: "This server isn't accepting changes for your account." });
    expect(sync.readOnly).toBe(true);
    refuse = false;
    sync.allowRetry();
    expect(await sync.run(() => moveMon(bank, 'a', 0, 5, NOW))).toEqual({ ok: true });
    expect(sync.readOnly).toBe(false);
  });

  it('a 403 that is not writes_disabled does not go read-only', async () => {
    const bank = await bankWith(mk('a', 0, 0));
    const sync = new Sync(bank, { push: async () => { throw new ApiError(403, 'email_not_verified', 'x'); } }, instant);
    expect((await sync.run(() => moveMon(bank, 'a', 0, 5, NOW))).ok).toBe(false);
    expect(sync.readOnly).toBe(false);
  });

  it('shows the server reason for rejected groups', async () => {
    const bank = await bankWith(mk('a', 0, 0));
    const sync = new Sync(bank, { push: async (r) => ({ version: 1, epoch: 'e', results: [{ clientGroupId: r.groups[0].clientGroupId, status: 'rejected', items: [], error: 'invalid_item', message: 'Box names are at most 24 characters.' }] }) }, instant);
    expect(await sync.run(() => moveMon(bank, 'a', 0, 5, NOW))).toEqual({ ok: false, message: 'Box names are at most 24 characters.' });
  });

  it('a null change pushes nothing', async () => {
    const bank = await bankWith(mk('a', 0, 0));
    let pushes = 0;
    const sync = new Sync(bank, { push: async () => { pushes++; return {} as PushResponse; } }, instant);
    expect(await sync.run(() => moveMany(bank, ['a'], 0, NOW))).toEqual({ ok: true });
    expect(pushes).toBe(0);
  });

  it('runs one change at a time so versions never race', async () => {
    const bank = await bankWith(mk('a', 0, 0), mk('b', 0, 1));
    const order: string[] = [];
    let release!: () => void;
    const gate = new Promise<void>((r) => (release = r));
    const sync = new Sync(bank, { push: async (r) => {
      order.push(r.groups[0].writes[0].id);
      if (order.length === 1) await gate;
      return { version: 9, epoch: 'e', results: [{ clientGroupId: r.groups[0].clientGroupId, status: 'accepted', items: r.groups[0].writes.map((w, i) => ({ kind: w.kind, id: w.id, version: 9 + i, deleted: false, value: w.value })) }] };
    } }, instant);
    const first = sync.run(() => moveMon(bank, 'a', 0, 5, NOW));
    const second = sync.run(() => moveMon(bank, 'b', 0, 6, NOW));
    release();
    await Promise.all([first, second]);
    expect(order).toEqual(['a', 'b']);
    expect((bank.mon('b')!.value as MonValue).slot).toBe(6);
  });

  it('a failing first change rolls back alone; the queued one still lands', async () => {
    const bank = await bankWith(mk('a', 0, 0), mk('b', 0, 1));
    let call = 0;
    const sync = new Sync(bank, { push: async (r) => {
      if (++call === 1) return { version: 1, epoch: 'e', results: [{ clientGroupId: r.groups[0].clientGroupId, status: 'rejected', items: [], error: 'invalid_item', message: 'no' }] };
      return { version: 5, epoch: 'e', results: [{ clientGroupId: r.groups[0].clientGroupId, status: 'accepted', items: r.groups[0].writes.map((w) => ({ kind: w.kind, id: w.id, version: 5, deleted: false, value: w.value })) }] };
    } }, instant);
    const [first, second] = await Promise.all([sync.run(() => moveMon(bank, 'a', 0, 5, NOW)), sync.run(() => moveMon(bank, 'b', 0, 6, NOW))]);
    expect(first.ok).toBe(false);
    expect(second.ok).toBe(true);
    expect(bank.monsIn(0)[0]?.id).toBe('a');
    expect(bank.monsIn(0)[6]?.id).toBe('b');
  });
});

const accept = (r: PushRequest, version = 5): PushResponse => ({ version, epoch: 'e', results: [{ clientGroupId: r.groups[0].clientGroupId, status: 'accepted',
  items: r.groups[0].writes.map((w) => ({ kind: w.kind, id: w.id, version, deleted: !!w.deleted, value: w.value })) }] });
const reject = (r: PushRequest): PushResponse => ({ version: 1, epoch: 'e', results: [{ clientGroupId: r.groups[0].clientGroupId, status: 'rejected', items: [], error: 'invalid_item', message: 'no' }] });

describe('Sync, more', () => {
  it('releases at the held version', async () => {
    const bank = await bankWith(mk('a', 0, 0));
    const sync = new Sync(bank, { push: async (r) => (r.groups[0].writes[0].baseVersion === 1 ? accept(r) : ({ version: 1, epoch: 'e', results: [{ clientGroupId: r.groups[0].clientGroupId, status: 'conflict', items: [] }] })) }, instant);
    expect(await sync.run(() => release(bank, ['a']))).toEqual({ ok: true });
    expect(bank.mon('a')).toBeUndefined();
  });

  it('a rejected release restores the item with its version', async () => {
    const bank = await bankWith(mk('a', 0, 0));
    const sync = new Sync(bank, { push: async (r) => reject(r) }, instant);
    expect((await sync.run(() => release(bank, ['a']))).ok).toBe(false);
    expect(bank.version('mon:a')).toBe(1);
    expect(bank.monsIn(0)[0]?.id).toBe('a');
  });

  it('builds queued changes against the state after earlier rollbacks', async () => {
    const bank = await bankWith(mk('a', 0, 0), mk('c', 1, 0));
    const sent: PushRequest[] = [];
    let open!: () => void;
    const gate = new Promise<void>((r) => (open = r));
    const sync = new Sync(bank, { push: async (r) => { sent.push(r); if (sent.length === 1) { await gate; return reject(r); } return accept(r); } }, instant);
    const first = sync.run(() => moveMon(bank, 'a', 1, 0, NOW)); // swaps with c
    const second = sync.run(() => moveMon(bank, 'c', 0, 0, NOW));
    open();
    expect((await first).ok).toBe(false);
    await second;
    const spots = bank.allMons().map((m) => `${m.value.box}/${m.value.slot}`);
    expect(new Set(spots).size).toBe(spots.length);
    for (const r of sent.slice(1)) {
      const positions = r.groups[0].writes.map((w) => (w.value as MonValue).slot + '/' + (w.value as MonValue).box);
      expect(new Set(positions).size).toBe(positions.length);
    }
  });

  it('retries a 5xx then succeeds', async () => {
    const bank = await bankWith(mk('a', 0, 0));
    let calls = 0;
    const sync = new Sync(bank, { push: async (r) => { if (++calls < 3) throw new ApiError(503, 'x', 'x'); return accept(r, 2); } }, instant);
    expect(await sync.run(() => moveMon(bank, 'a', 0, 5, NOW))).toEqual({ ok: true });
    expect(calls).toBe(3);
  });

  it('reloads on 410 and still resolves', async () => {
    const bank = await bankWith(mk('a', 0, 0));
    const sync = new Sync(bank, { push: async () => { throw new ApiError(410, 'gone', 'x'); } }, instant);
    expect((await sync.run(() => moveMon(bank, 'a', 0, 5, NOW))).ok).toBe(false);
    expect(bank.monsIn(0)[0]?.id).toBe('a');
  });

  it('resolves when the 410 reload fails', async () => {
    let fail = false;
    const bank = new Bank({ changes: async () => { if (fail) throw new ApiError(0, 'offline', 'x'); return { items: [mk('a', 0, 0)], next: 1, hasMore: false, epoch: 'e' }; } });
    await bank.load();
    const connectivity = new Connectivity(() => false);
    const sync = new Sync(bank, { push: async () => { fail = true; throw new ApiError(410, 'gone', 'x'); } }, { connectivity, ...instant });
    expect(await sync.run(() => moveMon(bank, 'a', 0, 5, NOW))).toEqual({ ok: false, message: "Couldn't save that change. Try again." });
    expect(connectivity.offline).toBe(true);
  });

  it('a 410 reload refused for another reason (not unreachable) does not count as offline', async () => {
    let fail = false;
    const bank = new Bank({ changes: async () => { if (fail) throw new ApiError(403, 'email_not_verified', 'x'); return { items: [mk('a', 0, 0)], next: 1, hasMore: false, epoch: 'e' }; } });
    await bank.load();
    const connectivity = new Connectivity(() => false);
    const sync = new Sync(bank, { push: async () => { fail = true; throw new ApiError(410, 'gone', 'x'); } }, { connectivity, ...instant });
    expect((await sync.run(() => moveMon(bank, 'a', 0, 5, NOW))).ok).toBe(false);
    expect(connectivity.offline).toBe(false);
  });

  it('makes a v4 UUID without crypto.randomUUID', () => {
    const real = crypto;
    vi.stubGlobal('crypto', { getRandomValues: (a: Uint8Array<ArrayBuffer>) => real.getRandomValues(a) });
    expect(uuid()).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    vi.unstubAllGlobals();
  });

  it('reports NoRoom as its own message and keeps the queue going', async () => {
    const full = Array.from({ length: 30 }, (_, i) => mk(`f${i}`, 1, i));
    const bank = await bankWith(mk('a', 0, 0), mk('b', 0, 1), ...full);
    const sync = new Sync(bank, { push: async (r) => accept(r) }, instant);
    const first = sync.run(() => sendToBox(bank, 'a', 1, NOW));
    const second = sync.run(() => moveMon(bank, 'b', 0, 6, NOW));
    expect(await first).toEqual({ ok: false, message: 'Box 2 has 0 free slots.' });
    expect(await second).toEqual({ ok: true });
    expect(bank.monsIn(0)[0]?.id).toBe('a');
  });

  it('does not roll back over a newer version delivered during the push', async () => {
    const bank = await bankWith(mk('a', 0, 0));
    let open!: () => void;
    const gate = new Promise<void>((r) => (open = r));
    const sync = new Sync(bank, { push: async (r) => { await gate; return reject(r); } }, instant);
    const pending = sync.run(() => moveMon(bank, 'a', 0, 5, NOW));
    bank.apply([mk('a', 2, 2, 4)]);
    open();
    expect((await pending).ok).toBe(false);
    expect(bank.version('mon:a')).toBe(4);
    expect(bank.monsIn(2)[2]?.id).toBe('a');
  });
});
