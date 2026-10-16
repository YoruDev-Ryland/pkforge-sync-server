import { api, friendlyError } from './api';
import type { ReleasedMon, ReleasedResponse } from './protocol';

export type ReleasedFetcher = () => Promise<ReleasedResponse>;

/**
 * Recently released Pokémon, fetched when the view opens and again whenever the Bank changes while it is open.
 * `retentionDays` is the server's window (0: releases are final); the release dialog mentions it.
 */
export class Released {
  items = $state.raw<ReleasedMon[]>([]);
  retentionDays = $state<number | null>(null);
  loaded = $state(false);
  error = $state<string | null>(null);
  #running: Promise<void> | null = null;
  #again = false;
  #generation = 0;

  constructor(private fetcher: ReleasedFetcher = () => api<ReleasedResponse>('/v1/bank/released')) {}

  /** Fetches the list; a call while one is under way fetches once more after it, so nothing newer is missed. */
  load(): Promise<void> {
    if (this.#running) { this.#again = true; return this.#running; }
    const generation = this.#generation;
    const run = async () => {
      do {
        this.#again = false;
        try {
          const r = await this.fetcher();
          if (generation !== this.#generation) return; // signed out meanwhile
          this.items = r.items; this.retentionDays = r.retentionDays; this.loaded = true; this.error = null;
        } catch (e) {
          if (generation !== this.#generation) return;
          this.error = friendlyError(e);
        }
      } while (this.#again);
    };
    const p: Promise<void> = run().finally(() => { if (this.#running === p) this.#running = null; });
    return (this.#running = p);
  }

  /** The server's window in days for the release dialog; null when unknown (asked once, waiting at most `wait` ms). */
  async windowDays(wait = 600): Promise<number | null> {
    if (this.retentionDays === null) await Promise.race([this.load(), new Promise((r) => setTimeout(r, wait))]);
    return this.retentionDays;
  }

  /** Signing out: forget this account's list. */
  reset(): void {
    this.#generation++;
    this.#running = null; this.#again = false;
    this.items = []; this.retentionDays = null; this.loaded = false; this.error = null;
  }
}

export const released = new Released();

/** The release dialog's last sentence: the window when there is one, else where releases can still be undone. */
export function releaseNote(days: number | null, one: boolean): string {
  const it = one ? 'it' : 'they';
  if (days && days > 0) return `You can restore ${one ? 'it' : 'them'} for ${days} ${days === 1 ? 'day' : 'days'} from Recently released.`;
  return `Each app keeps Bank snapshots, so ${it} can still be restored from a phone.`;
}
