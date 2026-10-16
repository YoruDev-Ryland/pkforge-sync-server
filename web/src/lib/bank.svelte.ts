import { ApiError } from './api';
import { BOXES_KEY, SETTINGS_KEY, SLOTS_PER_BOX, key, type BoxesValue, type ChangesResponse, type ItemKind, type MonValue, type SettingsValue, type SyncItem } from './protocol';

export interface Held { kind: ItemKind; id: string; version: number; value: unknown }
export type MonEntry = { id: string; version: number; value: MonValue };
export interface Fetcher { changes(since: number, epoch: string | null): Promise<ChangesResponse> }

export function boxLabel(boxes: BoxesValue, box: number): string {
  const name = boxes.names?.[box];
  return name ? name : `Box ${String(box + 1).padStart(2, '0')}`;
}

/** The Bank as this browser last saw it: live items by key, plus the sync cursor. */
export class Bank {
  loaded = $state(false);
  cursor = $state(0);
  epoch = $state<string | null>(null);
  items = $state.raw(new Map<string, Held>());

  readonly boxes: BoxesValue = $derived((this.items.get(BOXES_KEY)?.value as BoxesValue | undefined) ?? { count: 1 });
  readonly boxCount: number = $derived(Math.max(1, this.boxes.count));
  readonly settings: SettingsValue = $derived((this.items.get(SETTINGS_KEY)?.value as SettingsValue | undefined) ?? {});
  #byBox = $derived.by(() => {
    const map = new Map<number, (MonEntry | undefined)[]>();
    for (const held of this.items.values()) {
      if (held.kind !== 'mon') continue;
      const v = held.value as MonValue;
      let row = map.get(v.box);
      if (!row) map.set(v.box, (row = new Array(SLOTS_PER_BOX).fill(undefined)));
      row[v.slot] = { id: held.id, version: held.version, value: v };
    }
    return map;
  });

  constructor(private fetcher: Fetcher) {}

  monsIn(box: number): (MonEntry | undefined)[] {
    return this.#byBox.get(box) ?? new Array(SLOTS_PER_BOX).fill(undefined);
  }

  mon(id: string): MonEntry | undefined {
    const held = this.items.get(key('mon', id));
    return held ? { id, version: held.version, value: held.value as MonValue } : undefined;
  }

  allMons(): MonEntry[] {
    return [...this.items.values()].filter((h) => h.kind === 'mon').map((h) => ({ id: h.id, version: h.version, value: h.value as MonValue }));
  }

  version(k: string): number { return this.items.get(k)?.version ?? 0; }

  /** Forgets the Bank (signing out), so the next account starts clean. */
  reset(): void {
    this.items = new Map(); this.cursor = 0; this.epoch = null; this.loaded = false;
  }

  async load(): Promise<void> {
    const items = new Map<string, Held>();
    let since = 0, epoch: string | null = null;
    for (;;) {
      const page = await this.fetcher.changes(since, null);
      for (const item of page.items) put(items, item);
      since = page.next; epoch = page.epoch;
      if (!page.hasMore) break;
    }
    this.items = items; this.cursor = since; this.epoch = epoch; this.loaded = true;
  }

  async poll(): Promise<boolean> {
    try {
      let changed = false;
      for (;;) {
        const page = await this.fetcher.changes(this.cursor, this.epoch);
        if (page.items.length) { this.apply(page.items); changed = true; }
        this.cursor = page.next; this.epoch = page.epoch;
        if (!page.hasMore) return changed;
      }
    } catch (e) {
      if (e instanceof ApiError && e.status === 410) { await this.load(); return true; }
      throw e;
    }
  }

  apply(incoming: SyncItem[]): void {
    const items = new Map(this.items);
    let changed = false;
    for (const item of incoming) {
      const k = key(item.kind, item.id);
      if (items.has(k) && (items.get(k)?.version ?? 0) >= item.version) continue;
      changed = put(items, item) || changed;
    }
    if (changed) this.items = items;
  }

  /** Puts back exactly what was held (value and version), or removes the key. */
  restore(k: string, held: Held | undefined): void {
    const items = new Map(this.items);
    if (held) items.set(k, held); else items.delete(k);
    this.items = items;
  }

  setLocal(k: string, value: unknown | null): void {
    const items = new Map(this.items);
    const kind = k.slice(0, k.indexOf(':')) as ItemKind;
    const id = k.slice(k.indexOf(':') + 1);
    if (value === null) items.delete(k);
    else items.set(k, { kind, id, version: items.get(k)?.version ?? 0, value });
    this.items = items;
  }
}

function put(items: Map<string, Held>, item: SyncItem): boolean {
  const k = key(item.kind, item.id);
  if (item.deleted || item.value === null) return items.delete(k);
  items.set(k, { kind: item.kind as ItemKind, id: item.id, version: item.version, value: item.value });
  return true;
}
