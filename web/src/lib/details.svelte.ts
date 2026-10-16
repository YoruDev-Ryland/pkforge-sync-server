import { api } from './api';
import type { MonDetails } from './protocol';

export interface DetailsEntry { state: 'loading' | 'ready' | 'failed'; data?: MonDetails }

/** Inspector details, cached by blob hash for the session with one request in flight per blob.
 *  A failed request isn't kept: asking again tries again. */
export class DetailsCache {
  #entries = new Map<string, DetailsEntry>();

  constructor(private fetcher: (monId: string) => Promise<MonDetails>) {}

  get(monId: string, blob: string): DetailsEntry {
    const held = this.#entries.get(blob);
    if (held && held.state !== 'failed') return held;
    const entry = $state<DetailsEntry>({ state: 'loading' });
    this.#entries.set(blob, entry);
    this.fetcher(monId).then(
      (data) => { entry.data = data; entry.state = 'ready'; },
      () => { entry.state = 'failed'; },
    );
    return entry;
  }

  /** Signing out: another account's files must not show. */
  clear(): void { this.#entries.clear(); }
}

export const details = new DetailsCache((id) => api<MonDetails>(`/v1/web/mons/${encodeURIComponent(id)}/details`));
