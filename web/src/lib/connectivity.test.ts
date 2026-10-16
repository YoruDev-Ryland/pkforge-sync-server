import { describe, it, expect, vi, afterEach } from 'vitest';
import { ApiError } from './api';
import { Connectivity } from './connectivity.svelte';
import { Poller } from './poller';

describe('Connectivity', () => {
  it('a single failure is only a suspicion; the second in a row is offline', () => {
    const c = new Connectivity(() => false);
    c.failed();
    expect(c.offline).toBe(false);
    c.failed();
    expect(c.offline).toBe(true);
  });

  it('any success clears the suspicion and the banner at once', () => {
    const c = new Connectivity(() => false);
    c.failed();
    c.ok();
    c.failed();
    expect(c.offline).toBe(false);
    c.failed();
    expect(c.offline).toBe(true);
    c.ok();
    expect(c.offline).toBe(false);
  });

  it('a confirmed loss (a push out of retries) is offline at once', () => {
    const c = new Connectivity(() => false);
    c.lost();
    expect(c.offline).toBe(true);
  });

  it('ignores failures while the tab is hidden', () => {
    let hidden = true;
    const c = new Connectivity(() => hidden);
    c.failed(); c.failed(); c.lost();
    expect(c.offline).toBe(false);
    hidden = false;
    c.failed();
    expect(c.offline).toBe(false);
  });

  it('reset forgets everything', () => {
    const c = new Connectivity(() => false);
    c.failed(); c.failed();
    c.reset();
    expect(c.offline).toBe(false);
    c.failed();
    expect(c.offline).toBe(false);
  });
});

describe('Connectivity with the Poller', () => {
  afterEach(() => vi.useRealTimers());

  function setup() {
    const c = new Connectivity(() => false);
    const bank = { loaded: true, fail: null as unknown, polls: 0,
      async poll() { bank.polls++; if (bank.fail) throw bank.fail; return false; }, async load() {} };
    const poller = new Poller(bank, (e) => {
      if (e === null) c.ok();
      else if (e instanceof ApiError && (e.status === 0 || e.status >= 500)) c.failed();
    });
    return { c, bank, poller };
  }

  it('a blip (one failed poll, then the 5 s retry works) never shows the banner', async () => {
    vi.useFakeTimers();
    const { c, bank, poller } = setup();
    poller.start();
    bank.fail = new ApiError(0, 'offline', 'x');
    await vi.advanceTimersByTimeAsync(30_000);
    expect(c.offline).toBe(false);
    bank.fail = null;
    await vi.advanceTimersByTimeAsync(5_000);
    expect(bank.polls).toBe(2);
    expect(c.offline).toBe(false);
    poller.stop();
  });

  it('a failed poll whose retry fails too shows the banner, and the next good poll clears it', async () => {
    vi.useFakeTimers();
    const { c, bank, poller } = setup();
    poller.start();
    bank.fail = new ApiError(502, 'http_502', 'x');
    await vi.advanceTimersByTimeAsync(30_000);
    expect(c.offline).toBe(false);
    await vi.advanceTimersByTimeAsync(5_000);
    expect(c.offline).toBe(true);
    bank.fail = null;
    await vi.advanceTimersByTimeAsync(10_000);
    expect(c.offline).toBe(false);
    poller.stop();
  });
});
