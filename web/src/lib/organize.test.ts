import { describe, it, expect, vi, beforeEach } from 'vitest';
import { NoRoom, type Change } from './changes';

// A stub Sync that runs the builder at once (like the real queue when idle) and answers with `next`.
const h = vi.hoisted(() => {
  const state = {
    next: { ok: true } as { ok: true } | { ok: false; message: string },
    ui: { box: 0, selected: new Set<number>([0]), anchor: 0, inspectorTab: 'info' },
    path: '/bank/1',
    toasts: [] as string[],
  };
  const sync = {
    run: async (build: () => unknown) => {
      try { if (!build()) return { ok: true }; } catch (e) { return { ok: false, message: e instanceof Error ? e.message : 'x' }; }
      return state.next;
    },
  };
  return { state, sync };
});

vi.mock('./app.svelte', () => ({
  app: { offline: false, readOnly: false },
  bank: { boxes: { count: 3, names: ['Keepers', null, null] } },
  sync: h.sync,
  ui: h.state.ui,
  toast: (m: string) => h.state.toasts.push(m),
}));
vi.mock('./router.svelte', () => ({
  router: { get path() { return h.state.path; }, go: (p: string) => { h.state.path = p; } },
}));

const { act, fullAsName, selectOnly } = await import('./organize.svelte');
const change = (label: string): Change => ({ group: { clientGroupId: 'g', writes: [] }, apply: new Map(), label });

beforeEach(() => {
  h.state.next = { ok: true };
  Object.assign(h.state.ui, { box: 0, selected: new Set([2]), anchor: 2 });
  h.state.path = '/bank/1';
  h.state.toasts.length = 0;
});

describe('act', () => {
  it('applies the view change and toasts the label (or done) when the server accepts', async () => {
    expect(await act(() => change('Moved'), { applied: () => selectOnly([5]), done: () => 'Moved LEAFY' })).toBe(true);
    expect([...h.state.ui.selected]).toEqual([5]);
    expect(h.state.toasts).toEqual(['Moved LEAFY']);
    expect(await act(() => change('Box added'))).toBe(true);
    expect(h.state.toasts.at(-1)).toBe('Box added');
  });

  it('says nothing for a no-op', async () => {
    expect(await act(() => null, { applied: () => selectOnly([5]) })).toBe(false);
    expect(h.state.toasts).toEqual([]);
    expect([...h.state.ui.selected]).toEqual([2]);
  });

  it('puts the selection, open box and address back when the server says no', async () => {
    h.state.next = { ok: false, message: 'That box changed on another device. Try again.' };
    const ok = await act(() => change('Boxes reordered'), {
      applied: () => { h.state.ui.box = 2; h.state.path = '/bank/3'; selectOnly([7]); },
    });
    expect(ok).toBe(false);
    expect(h.state.ui.box).toBe(0);
    expect(h.state.path).toBe('/bank/1');
    expect([...h.state.ui.selected]).toEqual([2]);
    expect(h.state.ui.anchor).toBe(2);
    expect(h.state.toasts).toEqual(['That box changed on another device. Try again.']);
  });

  it("leaves the view alone if the user moved on while it was saving", async () => {
    let finish!: (o: { ok: false; message: string }) => void;
    const run = h.sync.run;
    h.sync.run = async (build) => { build(); return new Promise((r) => (finish = r)); };
    const pending = act(() => change('Moved'), { applied: () => selectOnly([7]) });
    selectOnly([9]); // a click while the push is in flight
    finish({ ok: false, message: 'nope' });
    await pending;
    h.sync.run = run;
    expect([...h.state.ui.selected]).toEqual([9]);
  });

  it('toasts NoRoom as "{Box} is full" through fullAsName', async () => {
    expect(await act(fullAsName(0, () => { throw new NoRoom(0, 0); }))).toBe(false);
    expect(await act(fullAsName(1, () => { throw new NoRoom(1, 2); }))).toBe(false);
    expect(h.state.toasts).toEqual(['Keepers is full', 'Box 02 only has 2 free slots']);
  });
});
