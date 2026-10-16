import { describe, it, expect, vi, afterEach } from 'vitest';
import { ApiError } from './api';
import { LiveStream, SseParser, type SseEvent } from './events';

const all = (chunks: string[]) => {
  const parser = new SseParser();
  return chunks.flatMap((c) => parser.push(c));
};

describe('SseParser', () => {
  it('reads events, several per chunk', () => {
    expect(all(['event: ready\ndata: {"version":3}\n\nevent: bank\ndata: {"version":4}\n\n'])).toEqual([
      { event: 'ready', data: '{"version":3}' },
      { event: 'bank', data: '{"version":4}' },
    ]);
  });

  it('handles a chunk boundary anywhere', () => {
    const text = 'event: ready\ndata: {"version":3}\n\n: ping\n\nevent: prefs\ndata: {}\n\n';
    const whole = all([text]);
    for (let i = 1; i < text.length; i++) expect(all([text.slice(0, i), text.slice(i)])).toEqual(whole);
    expect(all([...text])).toEqual(whole);
  });

  it('accepts CRLF and CR line endings, even split between chunks', () => {
    expect(all(['event: a\r\ndata: 1\r\n\r\nevent: b\rdata: 2\r\r'])).toEqual([{ event: 'a', data: '1' }, { event: 'b', data: '2' }]);
    expect(all(['event: a\r', '\ndata: 1\r', '\n\r', '\n'])).toEqual([{ event: 'a', data: '1' }]);
  });

  it('ignores comments, unknown fields and events without data', () => {
    expect(all([': ping\n\nid: 7\nretry: 10\nevent: x\n\nevent: y\ndata: 1\n\n'])).toEqual([{ event: 'y', data: '1' }]);
  });

  it('joins data lines, keeps colons in values, strips one leading space and defaults the name to message', () => {
    expect(all(['data:a\ndata:  b\ndata: c: d\n\n'])).toEqual([{ event: 'message', data: 'a\n b\nc: d' }]);
  });

  it('holds an unfinished event until its blank line arrives', () => {
    const parser = new SseParser();
    expect(parser.push('event: bank\ndata: {}\n')).toEqual([]);
    expect(parser.push('\n')).toEqual([{ event: 'bank', data: '{}' }]);
  });
});

/** A fake server stream: `send` text, `end` it, or `fail` it; aborting the signal errors the body like fetch does. */
function fakeServer() {
  const opened: { send(text: string): void; end(): void; fail(): void; signal: AbortSignal }[] = [];
  let refuse: unknown = null;
  const open = vi.fn(async (signal: AbortSignal) => {
    if (refuse) throw refuse;
    let ctrl!: ReadableStreamDefaultController<Uint8Array>;
    const body = new ReadableStream<Uint8Array>({ start: (c) => { ctrl = c; } });
    const enc = new TextEncoder();
    signal.addEventListener('abort', () => { try { ctrl.error(new DOMException('aborted', 'AbortError')); } catch { /* closed */ } });
    opened.push({ send: (t) => ctrl.enqueue(enc.encode(t)), end: () => ctrl.close(), fail: () => ctrl.error(new TypeError('network')), signal });
    return new Response(body);
  });
  return {
    open, opened,
    get last() { return opened[opened.length - 1]; },
    refuse(e: unknown) { refuse = e; },
  };
}

const READY = 'event: ready\ndata: {"version":1}\n\n';

describe('LiveStream', () => {
  afterEach(() => vi.useRealTimers());

  function setup(server = fakeServer()) {
    const seen: SseEvent[] = [];
    const states: boolean[] = [];
    const opens: number[] = [];
    const failures: unknown[] = [];
    let n = 0;
    const live = new LiveStream({
      open: server.open,
      onOpen: () => opens.push(++n),
      onEvent: (e) => seen.push(e),
      onState: (c) => states.push(c),
      onFailure: (e) => failures.push(e),
      random: () => 0.5, // no jitter
    });
    return { live, server, seen, states, opens, failures };
  }

  it('is connected once ready arrives, then hands over events', async () => {
    vi.useFakeTimers();
    const { live, server, seen, states, opens } = setup();
    live.start();
    await vi.advanceTimersByTimeAsync(0);
    expect(server.open).toHaveBeenCalledTimes(1);
    expect(live.connected).toBe(false);
    server.last.send(READY);
    await vi.advanceTimersByTimeAsync(0);
    expect(live.connected).toBe(true);
    expect(states).toEqual([true]);
    expect(opens).toEqual([1]);
    server.last.send('event: bank\ndata: {"version":2}\n\n');
    await vi.advanceTimersByTimeAsync(0);
    expect(seen).toEqual([{ event: 'bank', data: '{"version":2}' }]);
    live.stop();
  });

  it('reconnects with growing delays after failures, and resets once a connection lasts', async () => {
    vi.useFakeTimers();
    const { live, server, failures, opens } = setup();
    server.refuse(new ApiError(0, 'offline', 'x'));
    live.start();
    await vi.advanceTimersByTimeAsync(0);
    const gaps: number[] = [];
    for (let i = 0; i < 6; i++) {
      const before = server.open.mock.calls.length;
      let waited = 0;
      while (server.open.mock.calls.length === before) { await vi.advanceTimersByTimeAsync(500); waited += 500; }
      gaps.push(waited);
    }
    expect(gaps).toEqual([1000, 2000, 5000, 10000, 30000, 30000]);
    expect(failures.length).toBe(7); // the first try and six retries

    server.refuse(null);
    await vi.advanceTimersByTimeAsync(30_000);
    server.last.send(READY);
    await vi.advanceTimersByTimeAsync(30_000);
    server.last.send(': ping\n\n'); // under 60 s of silence, or the watchdog would drop it
    await vi.advanceTimersByTimeAsync(30_000);
    expect(opens.length).toBe(1);
    const calls = server.open.mock.calls.length;
    server.last.end(); // the server closed a long-lived stream: back to the first delay
    await vi.advanceTimersByTimeAsync(999);
    expect(server.open.mock.calls.length).toBe(calls);
    await vi.advanceTimersByTimeAsync(1);
    expect(server.open.mock.calls.length).toBe(calls + 1);
    server.last.send(READY);
    await vi.advanceTimersByTimeAsync(0);
    expect(opens.length).toBe(2); // every (re)connect reports, so the caller can catch up
    live.stop();
  });

  it('adds jitter to the delay', async () => {
    vi.useFakeTimers();
    const server = fakeServer();
    server.refuse(new ApiError(0, 'offline', 'x'));
    const live = new LiveStream({ open: server.open, onOpen() {}, onEvent() {}, random: () => 1 });
    live.start();
    await vi.advanceTimersByTimeAsync(1100);
    expect(server.open).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(100);
    expect(server.open).toHaveBeenCalledTimes(2);
    live.stop();
  });

  it('a stream that breaks is not a failure; only connect attempts that never got ready are', async () => {
    vi.useFakeTimers();
    const { live, server, failures, states } = setup();
    live.start();
    await vi.advanceTimersByTimeAsync(0);
    server.last.send(READY);
    await vi.advanceTimersByTimeAsync(0);
    server.last.fail();
    await vi.advanceTimersByTimeAsync(0);
    expect(states).toEqual([true, false]);
    expect(failures).toEqual([]);
    server.refuse(new ApiError(503, 'http_503', 'x'));
    await vi.advanceTimersByTimeAsync(1000);
    expect(failures).toHaveLength(1);
    live.stop();
  });

  it('stops retrying on errors that waiting will not fix, until the tab comes back', async () => {
    vi.useFakeTimers();
    const { live, server, failures } = setup();
    server.refuse(new ApiError(404, 'http_404', 'x'));
    live.start();
    await vi.advanceTimersByTimeAsync(120_000);
    expect(server.open).toHaveBeenCalledTimes(1);
    expect(failures).toHaveLength(1);
    server.refuse(null);
    document.dispatchEvent(new Event('visibilitychange'));
    await vi.advanceTimersByTimeAsync(0);
    expect(server.open).toHaveBeenCalledTimes(2);
    live.stop();
  });

  it('reconnects at once when the browser comes online or the tab becomes visible', async () => {
    vi.useFakeTimers();
    const { live, server } = setup();
    server.refuse(new ApiError(0, 'offline', 'x'));
    live.start();
    await vi.advanceTimersByTimeAsync(1000);
    await vi.advanceTimersByTimeAsync(2000);
    expect(server.open).toHaveBeenCalledTimes(3); // now waiting 5 s
    server.refuse(null);
    dispatchEvent(new Event('online'));
    await vi.advanceTimersByTimeAsync(0);
    expect(server.open).toHaveBeenCalledTimes(4);
    server.last.send(READY);
    await vi.advanceTimersByTimeAsync(0);
    // Already connected: waking does nothing.
    document.dispatchEvent(new Event('visibilitychange'));
    await vi.advanceTimersByTimeAsync(0);
    expect(server.open).toHaveBeenCalledTimes(4);
    live.stop();
  });

  it('stop aborts the stream and never reconnects', async () => {
    vi.useFakeTimers();
    const { live, server, states } = setup();
    live.start();
    await vi.advanceTimersByTimeAsync(0);
    server.last.send(READY);
    await vi.advanceTimersByTimeAsync(0);
    live.stop();
    await vi.advanceTimersByTimeAsync(0);
    expect(server.last.signal.aborted).toBe(true);
    expect(live.connected).toBe(false);
    expect(states).toEqual([true, false]);
    dispatchEvent(new Event('online'));
    await vi.advanceTimersByTimeAsync(120_000);
    expect(server.open).toHaveBeenCalledTimes(1);
  });

  const hide = (hidden: boolean) => {
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => (hidden ? 'hidden' : 'visible') });
    document.dispatchEvent(new Event('visibilitychange'));
  };
  afterEach(() => { delete (document as { visibilityState?: unknown }).visibilityState; });

  it('a stream silent for 60 s (pings count as data) is dropped as a failure and reopened', async () => {
    vi.useFakeTimers();
    const { live, server, failures, states } = setup();
    live.start();
    await vi.advanceTimersByTimeAsync(0);
    server.last.send(READY);
    await vi.advanceTimersByTimeAsync(50_000);
    server.last.send(': ping\n\n');
    await vi.advanceTimersByTimeAsync(50_000);
    expect(live.connected).toBe(true); // 50 s since the ping
    const stalled = server.last;
    await vi.advanceTimersByTimeAsync(10_000);
    expect(stalled.signal.aborted).toBe(true);
    expect(states).toEqual([true, false]);
    expect(failures).toHaveLength(1);
    await vi.advanceTimersByTimeAsync(1000);
    expect(server.open).toHaveBeenCalledTimes(2);
    live.stop();
  });

  it('coming back to the tab reopens a connection that has been silent for over 60 s', async () => {
    vi.useFakeTimers();
    const { live, server } = setup();
    live.start();
    await vi.advanceTimersByTimeAsync(0);
    server.last.send(READY);
    await vi.advanceTimersByTimeAsync(0);
    // Timers in a background tab can be held back: the clock moves on without the watchdog firing.
    vi.setSystemTime(Date.now() + 61_000);
    document.dispatchEvent(new Event('visibilitychange'));
    await vi.advanceTimersByTimeAsync(0);
    expect(server.open).toHaveBeenCalledTimes(2);
    live.stop();
  });

  it('closes the stream 10 s after the tab is hidden and reopens it when the tab is visible again', async () => {
    vi.useFakeTimers();
    const { live, server, failures, states, opens } = setup();
    live.start();
    await vi.advanceTimersByTimeAsync(0);
    server.last.send(READY);
    await vi.advanceTimersByTimeAsync(0);

    hide(true);
    await vi.advanceTimersByTimeAsync(5_000);
    hide(false); // a quick look elsewhere keeps the stream
    hide(true);
    await vi.advanceTimersByTimeAsync(9_000);
    expect(live.connected).toBe(true);
    await vi.advanceTimersByTimeAsync(1_000);
    expect(server.last.signal.aborted).toBe(true);
    expect(live.connected).toBe(false);
    await vi.advanceTimersByTimeAsync(300_000);
    expect(server.open).toHaveBeenCalledTimes(1); // nothing while hidden
    expect(failures).toEqual([]);

    hide(false);
    await vi.advanceTimersByTimeAsync(0);
    expect(server.open).toHaveBeenCalledTimes(2);
    server.last.send(READY);
    await vi.advanceTimersByTimeAsync(0);
    expect(opens).toEqual([1, 2]); // the reconnect catches up
    expect(states).toEqual([true, false, true]);
    live.stop();
  });

  it('started in a hidden tab, it waits to be visible', async () => {
    vi.useFakeTimers();
    hide(true);
    const { live, server } = setup();
    live.start();
    await vi.advanceTimersByTimeAsync(60_000);
    expect(server.open).toHaveBeenCalledTimes(0);
    hide(false);
    await vi.advanceTimersByTimeAsync(0);
    expect(server.open).toHaveBeenCalledTimes(1);
    live.stop();
  });
});
