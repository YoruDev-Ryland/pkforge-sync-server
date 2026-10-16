import { ApiError, writesDisabledMessage } from './api';
import type { Bank, Held } from './bank.svelte';
import { NoRoom, type Change } from './changes';
import type { PushRequest, PushResponse } from './protocol';

export type Outcome = { ok: true } | { ok: false; message: string };
export interface Pusher { push(req: PushRequest): Promise<PushResponse> }
/** Hears whether pushes reach the server (see Connectivity). */
export interface Reachability { ok(): void; lost(): void }
const unreachable = (e: unknown) => e instanceof ApiError && (e.status === 0 || e.status >= 500);
const writesDisabled = (e: unknown): e is ApiError => e instanceof ApiError && e.status === 403 && e.code === 'writes_disabled';
const wait = (ms: number) => new Promise<void>((r) => setTimeout(r, ms));
const CHANGED = 'That box changed on another device. Try again.';
const FAILED = "Couldn't save that change. Try again.";
/** A restore after the release window ended: the server already removed the Pokémon's data. */
const GONE = "That Pokémon is no longer on the server, so it can't be restored.";

/** Sends one change group at a time: shown at once, confirmed or undone by the server's answer. */
export class Sync {
  /** The server refused changes for this account (403 writes_disabled); viewing and downloads keep working. */
  readOnly = $state(false);
  /** What the server said when it refused, for the banner. */
  readOnlyMessage = $state(writesDisabledMessage(null));
  #tail: Promise<unknown> = Promise.resolve();
  #pending = 0;
  #retries: number;
  #delay: (ms: number) => Promise<void>;
  #net: Reachability | undefined;

  constructor(private bank: Bank, private pusher: Pusher,
    opts: { retries?: number; delay?: (ms: number) => Promise<void>; connectivity?: Reachability } = {}) {
    this.#retries = opts.retries ?? 3;
    this.#delay = opts.delay ?? wait;
    this.#net = opts.connectivity;
  }

  /** The server says the account's standing changed: let the user try changes again (a refusal turns this back on). */
  allowRetry(): void { this.readOnly = false; }

  /** Queues a builder; it runs against the bank as it is when its turn comes, so versions are never stale. */
  run(build: () => Change | null): Promise<Outcome> {
    const start = () => this.#exec(build);
    const result = this.#pending === 0 ? start() : this.#tail.then(start);
    this.#pending++;
    const done = result.finally(() => { this.#pending--; });
    this.#tail = done;
    return done;
  }

  async #exec(build: () => Change | null): Promise<Outcome> {
    try {
      if (this.readOnly) return { ok: false, message: this.readOnlyMessage };
      const change = build();
      if (!change) return { ok: true };
      const before = new Map([...change.apply.keys()].map((k) => [k, this.bank.items.get(k)]));
      const written = new Map<string, Held | undefined>();
      for (const [k, value] of change.apply) { this.bank.setLocal(k, value); written.set(k, this.bank.items.get(k)); }
      return await this.#send(change, before, written);
    } catch (e) {
      return { ok: false, message: e instanceof NoRoom ? e.message : FAILED };
    }
  }

  async #send(change: Change, before: Map<string, Held | undefined>, written: Map<string, Held | undefined>): Promise<Outcome> {
    // Only undo keys still holding our optimistic write; a poll may have delivered something newer.
    const undo = () => { for (const [k, held] of before) if (this.bank.items.get(k) === written.get(k)) this.bank.restore(k, held); };
    const group = change.group;
    for (let attempt = 0; ; attempt++) {
      try {
        const response = await this.pusher.push({ groups: [group], since: this.bank.cursor, epoch: this.bank.epoch });
        this.#net?.ok();
        const result = response.results.find((r) => r.clientGroupId === group.clientGroupId)!;
        if (result.status === 'accepted') { this.readOnly = false; this.bank.apply(result.items); return { ok: true }; }
        undo();
        if (result.status === 'rejected') return { ok: false, message: result.error === 'missing_blob' ? GONE : result.message ?? FAILED };
        this.bank.apply(result.items.filter((i) => i.version > 0));
        await this.bank.poll().catch(() => false);
        return { ok: false, message: CHANGED };
      } catch (e) {
        if (writesDisabled(e)) { this.readOnlyMessage = writesDisabledMessage(e.message); this.readOnly = true; undo(); return { ok: false, message: this.readOnlyMessage }; }
        if (e instanceof ApiError && e.status === 410) { undo(); try { await this.bank.load(); } catch (le) { if (unreachable(le)) this.#net?.lost(); return { ok: false, message: FAILED }; } return { ok: false, message: CHANGED }; }
        const transient = !(e instanceof ApiError) || unreachable(e);
        if (!transient || attempt >= this.#retries) {
          // Out of retries against a server that can't be reached: that's confirmed, not a blip.
          if (unreachable(e)) this.#net?.lost();
          undo();
          return { ok: false, message: FAILED };
        }
        await this.#delay(500 * 2 ** attempt);
      }
    }
  }
}
