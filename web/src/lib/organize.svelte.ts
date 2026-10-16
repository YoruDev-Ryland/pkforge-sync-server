import { app, bank, sync, toast, ui } from './app.svelte';
import { boxLabel } from './bank.svelte';
import { NoRoom, release, type Change } from './changes';
import { released } from './released.svelte';
import { router } from './router.svelte';

/** What is being dragged right now: a Pokémon from the open box, or a box row in the sidebar. */
export const drag = $state<{ current: { kind: 'mon'; id: string; slot: number } | { kind: 'box'; box: number } | null }>({ current: null });

/** Changes are paused while offline or read-only; browsing keeps working. */
export const locked = () => app.offline || app.readOnly;
export const lockedTitle = () => (app.readOnly ? 'Read-only' : app.offline ? "Can't reach the server" : undefined);

export const nowUtc = () => new Date().toISOString();

/** The selection, set in one go. */
export function selectOnly(slots: number[], anchor = slots[0] ?? 0): void {
  ui.selected = new Set(slots.length ? slots : [anchor]);
  ui.anchor = anchor;
}

/**
 * Runs one organizer action through the sync queue. `build` runs when its turn comes (against the Bank as it is then);
 * `applied` runs right after it built a change, so the selection moves with the optimistic update, and is undone when
 * the server says no. Toasts `done(change)` (default: the change's label) or the reason it failed.
 */
export async function act(build: () => Change | null, opts: { done?: (c: Change) => string; applied?: () => void } = {}): Promise<boolean> {
  // Assigned inside the queued builder; typed by hand so TS doesn't narrow them to null.
  let change = null as Change | null;
  let before = null as { selected: Set<number>; anchor: number; box: number; path: string } | null;
  let after = null as { selected: Set<number>; box: number; path: string } | null;
  const outcome = await sync.run(() => {
    change = build();
    if (change && opts.applied) {
      before = { selected: ui.selected, anchor: ui.anchor, box: ui.box, path: router.path };
      opts.applied();
      after = { selected: ui.selected, box: ui.box, path: router.path };
    }
    return change;
  });
  if (!outcome.ok) {
    // Undo what `applied` did to the view (open box, address, selection), but only what nobody changed since.
    if (before && after) {
      if (router.path === after.path && after.path !== before.path) { ui.box = before.box; router.go(before.path, true); }
      else if (ui.box === after.box && after.box !== before.box) ui.box = before.box;
      if (ui.selected === after.selected && ui.box === before.box) { ui.selected = before.selected; ui.anchor = before.anchor; }
    }
    toast(outcome.message);
    return false;
  }
  if (change) toast(opts.done ? opts.done(change) : change.label);
  return !!change;
}

/** Wraps a NoRoom from a sidebar drop as "{Box} is full". */
export function fullAsName<T>(box: number, build: () => T): () => T {
  return () => {
    try { return build(); } catch (e) {
      if (e instanceof NoRoom) e.message = e.free === 0 ? `${boxLabel(bank.boxes, box)} is full` : `${boxLabel(bank.boxes, box)} only has ${e.free} free slots`;
      throw e;
    }
  };
}

/** The Pokémon waiting for the release dialog (the selection bar and the inspector both ask through this). */
export const releasing = $state<{ ids: string[] }>({ ids: [] });
/** The dialog names the server's restore window, so the first ask fetches it (briefly; the dialog opens either way). */
export const askRelease = async (ids: string[]) => { await released.windowDays(); releasing.ids = ids; };

export function confirmRelease(): void {
  const ids = releasing.ids;
  releasing.ids = [];
  const box = ui.box;
  const first = Math.min(...[...ui.selected].filter((s) => bank.monsIn(box)[s]), ui.anchor);
  void act(() => release(bank, ids), { applied: () => { if (ui.box === box) selectOnly([first]); } });
}
