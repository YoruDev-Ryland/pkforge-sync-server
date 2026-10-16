import { ApiError } from './api';

/** One server-sent event: its name (`message` when the server gave none) and its data lines joined by newlines. */
export interface SseEvent { event: string; data: string }

/**
 * Reads a `text/event-stream` body chunk by chunk. Lines may end in LF, CRLF or CR, and a chunk may end anywhere
 * (even between the CR and LF of one line break). Comments, `id` and `retry` are ignored; events without data are dropped.
 */
export class SseParser {
  #line = '';
  #afterCR = false;
  #event = '';
  #data: string[] = [];

  push(chunk: string): SseEvent[] {
    const out: SseEvent[] = [];
    let start = 0;
    for (let i = 0; i < chunk.length; i++) {
      const c = chunk[i];
      if (c === '\n' && this.#afterCR && i === start && this.#line === '') { // the LF of a CRLF split from its CR
        this.#afterCR = false;
        start = i + 1;
        continue;
      }
      this.#afterCR = false;
      if (c !== '\n' && c !== '\r') continue;
      this.#take(this.#line + chunk.slice(start, i), out);
      this.#line = '';
      if (c === '\r' && chunk[i + 1] === '\n') i++;
      else if (c === '\r') this.#afterCR = true;
      start = i + 1;
    }
    this.#line += chunk.slice(start);
    return out;
  }

  #take(line: string, out: SseEvent[]): void {
    if (line === '') {
      if (this.#data.length) out.push({ event: this.#event || 'message', data: this.#data.join('\n') });
      this.#event = '';
      this.#data = [];
      return;
    }
    if (line.startsWith(':')) return;
    const colon = line.indexOf(':');
    const field = colon < 0 ? line : line.slice(0, colon);
    let value = colon < 0 ? '' : line.slice(colon + 1);
    if (value.startsWith(' ')) value = value.slice(1);
    if (field === 'event') this.#event = value;
    else if (field === 'data') this.#data.push(value);
  }
}

export interface LiveOptions {
  /** Opens the stream; rejects with an ApiError when the server refuses or can't be reached. */
  open(signal: AbortSignal): Promise<Response>;
  /** Connected (the server's `ready` arrived), first time or again: catch up on anything missed meanwhile. */
  onOpen(): void;
  /** Every event after `ready`. */
  onEvent(e: SseEvent): void;
  /** Connected or not. */
  onState?(connected: boolean): void;
  /** A connection attempt failed before `ready` (a stream that breaks later is not a failure). */
  onFailure?(error: unknown): void;
  /** For tests: 0..1, spreads reconnects ±20 %. */
  random?: () => number;
}

const DELAYS = [1000, 2000, 5000, 10000, 30000];
/** A connection that lasted this long starts the delays over. */
const STEADY = 10_000;
/** The server pings every 25 s; this long without a byte means the connection is dead (e.g. half-open). */
const SILENCE = 60_000;
/** A tab hidden this long gives up its stream (browsers allow only a few connections per server); visible reopens it. */
const HIDDEN_GRACE = 10_000;
/** Errors that retrying soon won't fix (signed out, not verified, outdated, no such endpoint): wait for a wake-up. */
const halts = (e: unknown) => e instanceof ApiError && e.status >= 400 && e.status < 500 && e.status !== 408 && e.status !== 429;
const hidden = () => document.visibilityState === 'hidden';

/**
 * The server's change notifications: stays connected while the tab is visible, reconnecting with backoff, until
 * stopped. A silent connection is dropped after 60 s; a tab hidden for 10 s closes its stream until it is visible.
 */
export class LiveStream {
  connected = false;
  #started = false;
  #running = false;
  #halted = false;
  #paused = false;
  #stalled = false;
  #now = false;
  #attempt = 0;
  #lastByte = 0;
  #abort: AbortController | null = null;
  #sleep: { timer: ReturnType<typeof setTimeout>; done: () => void } | null = null;
  #watchdog: ReturnType<typeof setTimeout> | undefined;
  #hideTimer: ReturnType<typeof setTimeout> | undefined;

  #onVisibility = () => {
    clearTimeout(this.#hideTimer);
    if (!this.#started) return;
    if (hidden()) this.#hideTimer = setTimeout(() => this.#pause(), HIDDEN_GRACE);
    else this.#wake();
  };
  #onOnline = () => this.#wake();

  constructor(private opts: LiveOptions) {}

  start(): void {
    if (this.#started) return;
    this.#started = true;
    this.#halted = false;
    this.#attempt = 0;
    document.addEventListener('visibilitychange', this.#onVisibility);
    addEventListener('online', this.#onOnline);
    this.#paused = hidden();
    if (!this.#paused) void this.#loop();
  }

  stop(): void {
    this.#started = false;
    document.removeEventListener('visibilitychange', this.#onVisibility);
    removeEventListener('online', this.#onOnline);
    clearTimeout(this.#hideTimer);
    clearTimeout(this.#watchdog);
    this.#abort?.abort();
    this.#interrupt();
    this.#set(false);
  }

  /** Back to the tab or back online: reconnect now unless a healthy connection is open. */
  #wake(): void {
    if (!this.#started || hidden()) return;
    if (this.#paused) { this.#paused = false; this.#attempt = 0; void this.#loop(); return; }
    if (this.connected) {
      if (Date.now() - this.#lastByte > SILENCE) { this.#now = true; this.#stall(); }
      return;
    }
    this.#attempt = 0;
    if (this.#halted) { this.#halted = false; void this.#loop(); }
    else this.#interrupt();
  }

  #pause(): void {
    if (!this.#started || !hidden()) return;
    this.#paused = true;
    this.#abort?.abort();
    this.#interrupt();
  }

  #stall(): void {
    this.#stalled = true;
    this.#abort?.abort();
  }

  #touch(): void {
    this.#lastByte = Date.now();
    clearTimeout(this.#watchdog);
    this.#watchdog = setTimeout(() => this.#stall(), SILENCE);
  }

  async #loop(): Promise<void> {
    if (this.#running) return;
    this.#running = true;
    try {
      while (this.#started && !this.#paused) {
        const abort = new AbortController();
        this.#abort = abort;
        this.#stalled = false;
        let since = 0, error: unknown = null;
        try {
          const response = await this.opts.open(abort.signal);
          this.#touch();
          await this.#read(response, abort.signal, () => (since = Date.now()));
        } catch (e) { error = e; }
        clearTimeout(this.#watchdog);
        this.#abort = null;
        const wasConnected = this.connected;
        this.#set(false);
        if (!this.#started || this.#paused) return;
        const stalled = this.#stalled;
        if (stalled) error = new ApiError(0, 'offline', 'The server went quiet.');
        if (!wasConnected || stalled) this.opts.onFailure?.(error ?? new ApiError(0, 'offline', 'The stream closed before it was ready.'));
        if (error && halts(error)) { this.#halted = true; return; }
        if (wasConnected && !stalled && Date.now() - since >= STEADY) this.#attempt = 0;
        if (this.#now) { this.#now = false; continue; }
        const base = DELAYS[Math.min(this.#attempt++, DELAYS.length - 1)];
        await this.#wait(base * (0.8 + 0.4 * (this.opts.random ?? Math.random)()));
      }
    } finally { this.#running = false; }
  }

  async #read(response: Response, signal: AbortSignal, connectedAt: () => void): Promise<void> {
    if (!response.body) return;
    const reader = response.body.getReader();
    // Stopping ends a pending read even where aborting the fetch doesn't reach the body.
    signal.addEventListener('abort', () => { reader.cancel().catch(() => {}); }, { once: true });
    const decoder = new TextDecoder();
    const parser = new SseParser();
    for (;;) {
      const { done, value } = await reader.read();
      if (done || signal.aborted || !this.#started) return;
      this.#touch(); // any bytes, pings included, prove the connection is alive
      for (const e of parser.push(decoder.decode(value, { stream: true }))) {
        if (!this.#started) return;
        if (e.event === 'ready') {
          connectedAt();
          this.#set(true);
          this.opts.onOpen();
        } else if (this.connected) {
          this.opts.onEvent(e);
        }
      }
    }
  }

  #wait(ms: number): Promise<void> {
    return new Promise((done) => { this.#sleep = { timer: setTimeout(() => { this.#sleep = null; done(); }, ms), done }; });
  }

  #interrupt(): void {
    if (!this.#sleep) return;
    clearTimeout(this.#sleep.timer);
    const { done } = this.#sleep;
    this.#sleep = null;
    done();
  }

  #set(connected: boolean): void {
    if (this.connected === connected) return;
    this.connected = connected;
    this.opts.onState?.(connected);
  }
}
