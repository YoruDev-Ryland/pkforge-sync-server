import { ApiError } from './api';

export interface Pollable { loaded: boolean; load(): Promise<void>; poll(): Promise<boolean> }

const EVERY = 30_000, EVERY_LIVE = 120_000, FIRST_RETRY = 5_000, MAX_RETRY = 120_000;

/**
 * Keeps the Bank current: one poll or load at a time, every 30 s (2 minutes while the live stream is connected) and on
 * tab focus, backing off while unreachable.
 */
export class Poller {
  #running: Promise<void> | null = null;
  #timer: ReturnType<typeof setTimeout> | null = null;
  #started = false;
  #failures = 0;
  #halted = false;
  #again = false;
  #live = false;
  #wake = () => { if (document.visibilityState !== 'hidden') void this.poll(); };

  /** `halts`: errors that waiting won't fix (e.g. an unverified email); the timer stops until a focus or online poll succeeds. */
  constructor(private bank: Pollable, private onResult: (error: unknown | null) => void,
    private halts: (error: unknown) => boolean = () => false) {}

  /** Polls unless a poll or load is already running (then shares it); loads if the Bank never loaded. */
  poll(): Promise<void> {
    return this.#running ?? this.#run(() => (this.bank.loaded ? this.bank.poll() : this.bank.load()));
  }

  /** Something changed on the server: polls now, or exactly once more after the poll that is running (it may have read too early). */
  nudge(): void {
    if (this.#running) this.#again = true;
    else void this.poll();
  }

  /** While the live stream is connected, the timer is only a safety net. */
  setLive(live: boolean): void {
    if (this.#live === live) return;
    this.#live = live;
    if (!this.#running) this.#schedule();
  }

  /** Waits for whatever is running, then reloads from scratch. */
  async load(): Promise<void> {
    while (this.#running) await this.#running;
    return this.#run(() => this.bank.load());
  }

  start(): void {
    if (this.#started) return;
    this.#started = true;
    document.addEventListener('visibilitychange', this.#wake);
    addEventListener('online', this.#wake);
    this.#schedule();
  }

  stop(): void {
    this.#started = false;
    this.#again = false;
    this.#live = false;
    document.removeEventListener('visibilitychange', this.#wake);
    removeEventListener('online', this.#wake);
    if (this.#timer) clearTimeout(this.#timer);
    this.#timer = null;
  }

  #run(work: () => Promise<unknown>): Promise<void> {
    const run = (async () => {
      let error: unknown = null;
      try { await work(); } catch (e) { error = e; }
      const unreachable = error instanceof ApiError && (error.status === 0 || error.status >= 500);
      this.#failures = unreachable ? this.#failures + 1 : 0;
      this.#halted = error !== null && this.halts(error);
      this.onResult(error);
    })().finally(() => {
      this.#running = null;
      if (this.#again) { this.#again = false; void this.poll(); } else this.#schedule();
    });
    this.#running = run;
    return run;
  }

  #schedule(): void {
    if (this.#timer) clearTimeout(this.#timer);
    this.#timer = null;
    if (!this.#started || this.#halted) return;
    const delay = this.#failures ? Math.min(MAX_RETRY, FIRST_RETRY * 2 ** (this.#failures - 1)) : this.#live ? EVERY_LIVE : EVERY;
    this.#timer = setTimeout(() => void this.poll(), delay);
  }
}
