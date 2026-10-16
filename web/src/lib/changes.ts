import { BOXES_KEY, SETTINGS_KEY, key, type BoxesValue, type ChangeGroup, type ItemWrite, type MonValue, type ReleasedMon, type SettingsValue } from './protocol';
import { boxLabel, type MonEntry } from './bank.svelte';

export interface Change { group: ChangeGroup; apply: Map<string, unknown | null>; label: string }
export interface View {
  monsIn(box: number): (MonEntry | undefined)[]; mon(id: string): MonEntry | undefined; allMons(): MonEntry[];
  boxes: BoxesValue; boxCount: number; settings: SettingsValue; version(key: string): number;
}
export class NoRoom extends Error {
  constructor(public box: number, public free: number) { super(`Box ${box + 1} has ${free} free slots.`); }
}
export const MAX_NAME = 24;
export function uuid(): string {
  if (typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  // randomUUID needs a secure context; plain-http LAN servers get a v4 UUID from getRandomValues.
  const b = crypto.getRandomValues(new Uint8Array(16));
  b[6] = (b[6] & 0x0f) | 0x40; b[8] = (b[8] & 0x3f) | 0x80;
  const h = [...b].map((x) => x.toString(16).padStart(2, '0')).join('');
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
}

function build(v: View, values: [string, unknown | null][], label: string, newId: () => string): Change | null {
  if (values.length === 0) return null;
  const writes: ItemWrite[] = values.map(([k, value]) => {
    const [kind, id] = [k.slice(0, k.indexOf(':')), k.slice(k.indexOf(':') + 1)];
    return value === null ? { kind, id, baseVersion: v.version(k), value: null, deleted: true } : { kind, id, baseVersion: v.version(k), value };
  });
  return { group: { clientGroupId: newId(), writes }, apply: new Map(values), label };
}

const placed = (m: MonEntry, box: number, slot: number, now: string): MonValue => ({ ...m.value, box, slot, movedUtc: now });

export function freeSlots(v: View, box: number): number[] {
  return v.monsIn(box).flatMap((m, i) => (m ? [] : [i]));
}

export function moveMon(v: View, id: string, box: number, slot: number, now: string, newId = uuid): Change | null {
  const m = v.mon(id);
  if (!m || (m.value.box === box && m.value.slot === slot)) return null;
  const occupant = v.monsIn(box)[slot];
  const values: [string, unknown][] = [[key('mon', id), placed(m, box, slot, now)]];
  if (occupant) values.push([key('mon', occupant.id), placed(occupant, m.value.box, m.value.slot, now)]);
  return build(v, values, occupant ? 'Swapped' : 'Moved', newId);
}

export function sendToBox(v: View, id: string, box: number, now: string, newId = uuid): Change | null {
  const m = v.mon(id);
  if (!m || m.value.box === box) return null;
  const free = freeSlots(v, box);
  if (free.length === 0) throw new NoRoom(box, 0);
  return build(v, [[key('mon', id), placed(m, box, free[0], now)]], 'Moved', newId);
}

export function moveMany(v: View, ids: string[], box: number, now: string, newId = uuid): Change | null {
  const mons = ids.map((id) => v.mon(id)).filter((m): m is MonEntry => !!m && m.value.box !== box)
    .sort((a, b) => a.value.box - b.value.box || a.value.slot - b.value.slot);
  if (mons.length === 0) return null;
  const free = freeSlots(v, box);
  if (free.length < mons.length) throw new NoRoom(box, free.length);
  return build(v, mons.map((m, i) => [key('mon', m.id), placed(m, box, free[i], now)] as [string, unknown]), `Moved ${mons.length}`, newId);
}

export function release(v: View, ids: string[], newId = uuid): Change | null {
  const mons = ids.map((id) => v.mon(id)).filter((m): m is MonEntry => !!m);
  return build(v, mons.map((m) => [key('mon', m.id), null] as [string, null]), `Released ${mons.length}`, newId);
}

/** Every box is full: nowhere to put a restored Pokémon. */
export class BankFull extends NoRoom {
  constructor() { super(-1, 0); this.message = 'Every box is full. Make room, then restore.'; }
}

/**
 * Where a released Pokémon goes back to: its old box and slot if that's free, else the first empty slot from its old
 * box on (wrapping around to box 1); null when every box is full. A box that no longer exists starts at the last one.
 */
export function restoreSpot(v: View, was: { box: number; slot: number }): { box: number; slot: number } | null {
  if (was.box < v.boxCount && !v.monsIn(was.box)[was.slot]) return { box: was.box, slot: was.slot };
  const start = Math.max(0, Math.min(was.box, v.boxCount - 1));
  for (let i = 0; i < v.boxCount; i++) {
    const box = (start + i) % v.boxCount;
    const free = freeSlots(v, box);
    if (free.length) return { box, slot: free[0] };
  }
  return null;
}

/** Puts a released Pokémon back: the same id on top of its tombstone, with its old value and bytes, in `restoreSpot`. */
export function restoreMon(v: View, r: ReleasedMon, now: string, newId = uuid): Change | null {
  if (v.mon(r.id)) return null; // already back
  const spot = restoreSpot(v, r.value);
  if (!spot) throw new BankFull();
  const moved = spot.box !== r.value.box || spot.slot !== r.value.slot;
  const value: MonValue = { ...r.value, ...spot, movedUtc: moved ? now : r.value.movedUtc };
  return {
    group: { clientGroupId: newId(), writes: [{ kind: 'mon', id: r.id, baseVersion: r.version, value }] },
    apply: new Map([[key('mon', r.id), value]]),
    label: `Restored ${r.value.info.nickname} to ${boxLabel(v.boxes, spot.box)}`,
  };
}

const padded = <T>(list: readonly (T | null)[] | undefined, length: number): (T | null)[] =>
  Array.from({ length }, (_, i) => list?.[i] ?? null);
function layout(v: View, names: (string | null)[], wallpapers: (string | null)[], count: number): [string, unknown] {
  return [BOXES_KEY, { ...v.boxes, count, names, wallpapers }];
}

export function renameBox(v: View, box: number, name: string, newId = uuid): Change | null {
  if (box < 0 || box >= v.boxCount) return null;
  const clean = name.trim().slice(0, MAX_NAME) || null;
  const names = padded(v.boxes.names, Math.max(v.boxes.names?.length ?? 0, box + 1));
  if ((names[box] ?? null) === clean) return null;
  names[box] = clean;
  return build(v, [layout(v, names, [...(v.boxes.wallpapers ?? [])], v.boxCount)], 'Box renamed', newId);
}

export function setWallpaper(v: View, box: number, wallpaper: string | null, newId = uuid): Change | null {
  if (box < 0 || box >= v.boxCount) return null;
  const wallpapers = padded(v.boxes.wallpapers, Math.max(v.boxes.wallpapers?.length ?? 0, box + 1));
  if ((wallpapers[box] ?? null) === wallpaper) return null;
  wallpapers[box] = wallpaper;
  return build(v, [layout(v, [...(v.boxes.names ?? [])], wallpapers, v.boxCount)], 'Wallpaper changed', newId);
}

/** Moves box `from` to position `to`; everything between shifts by one. */
export function reorderBox(v: View, from: number, to: number, now: string, newId = uuid): Change | null {
  if (from === to) return null;
  const order = Array.from({ length: v.boxCount }, (_, i) => i);
  order.splice(to, 0, ...order.splice(from, 1));
  const newIndex = new Map(order.map((oldBox, newBox) => [oldBox, newBox]));
  return remap(v, (b) => newIndex.get(b) ?? b, order, v.boxCount, now, 'Boxes reordered', newId);
}

export function addBox(v: View, newId = uuid): Change {
  return build(v, [layout(v, [...(v.boxes.names ?? [])], [...(v.boxes.wallpapers ?? [])], v.boxCount + 1)], 'Box added', newId)!;
}

export function deleteBox(v: View, box: number, now: string, newId = uuid): Change | null {
  if (v.boxCount <= 1 || v.monsIn(box).some(Boolean)) return null;
  const order = Array.from({ length: v.boxCount }, (_, i) => i).filter((b) => b !== box);
  return remap(v, (b) => (b > box ? b - 1 : b === box ? -1 : b), order, v.boxCount - 1, now, 'Box deleted', newId);
}

/** Rewrites the layout in `order` (old indexes by new position), moves every Pokémon whose box index changes, and the start box. */
function remap(v: View, map: (oldBox: number) => number, order: number[], count: number, now: string, label: string, newId: () => string): Change | null {
  const names = padded(v.boxes.names, v.boxCount), wallpapers = padded(v.boxes.wallpapers, v.boxCount);
  const values: [string, unknown][] = [layout(v, order.map((o) => names[o]), order.map((o) => wallpapers[o]), count)];
  for (const m of v.allMons()) {
    const to = map(m.value.box);
    if (to !== m.value.box) values.push([key('mon', m.id), placed(m, to, m.value.slot, now)]);
  }
  const start = v.settings.livingDexStartBox;
  if (typeof start === 'number') {
    const moved = map(start);
    if (moved !== start) values.push([SETTINGS_KEY, { ...v.settings, livingDexStartBox: moved < 0 ? null : moved }]);
  }
  return build(v, values, label, newId);
}
