import { describe, it, expect, vi, afterEach } from 'vitest';
import { Poller } from './poller';
import { ApiError } from './api';

function gate() {
  let open!: () => void;
  const promise = new Promise<void>((r) => (open = r));
  return { promise, open };
}

function fakeBank(loaded = true) {
  const bank = {
    loaded, polls: 0, loads: 0, gate: null as ReturnType<typeof gate> | null, fail: null as unknown,
    async poll() { bank.polls++; await bank.gate?.promise; if (bank.fail) throw bank.fail; return false; },
    async load() { bank.loads++; await bank.gate?.promise; if (bank.fail) throw bank.fail; bank.loaded = true; },
  };
  return bank;
}

describe('Poller', () => {
  afterEach(() => vi.useRealTimers());

  it('never runs two polls at once', async () => {
    const bank = fakeBank();
    const poller = new Poller(bank, () => {});
    bank.gate = gate();
    const a = poller.poll(), b = poller.poll();
    bank.gate.open();
    await Promise.all([a, b]);
    expect(bank.polls).toBe(1);
  });

  it('skips a poll while a load runs, and a load waits for a running poll', async () => {
    const bank = fakeBank();
    const poller = new Poller(bank, () => {});
    bank.gate = gate();
    const p = poller.poll();
    const l = poller.load();
    await Promise.resolve();
    expect(bank.loads).toBe(0);
    bank.gate.open();
    await p; await l;
    expect(bank.loads).toBe(1);
    bank.gate = gate();
    const l2 = poller.load();
    await Promise.resolve();
    const p2 = poller.poll();
    bank.gate.open();
    await Promise.all([l2, p2]);
    expect(bank.polls).toBe(1);
    expect(bank.loads).toBe(2);
  });

  it('loads instead of polling when the Bank never loaded', async () => {
    const bank = fakeBank(false);
    await new Poller(bank, () => {}).poll();
    expect([bank.loads, bank.polls]).toEqual([1, 0]);
  });

  it('reports each outcome', async () => {
    const bank = fakeBank();
    const seen: unknown[] = [];
    const poller = new Poller(bank, (e) => seen.push(e));
    await poller.poll();
    bank.fail = new ApiError(401, 'unauthorized', 'x');
    await poller.poll();
    expect(seen).toEqual([null, bank.fail]);
  });

  it('polls every 30 s, and backs off up to 2 minutes while unreachable', async () => {
    vi.useFakeTimers();
    const bank = fakeBank();
    const poller = new Poller(bank, () => {});
    poller.start();
    await vi.advanceTimersByTimeAsync(30_000);
    expect(bank.polls).toBe(1);
    bank.fail = new ApiError(0, 'offline', 'x');
    await vi.advanceTimersByTimeAsync(30_000);
    expect(bank.polls).toBe(2);
    const gaps: number[] = [];
    for (let i = 0; i < 7; i++) {
      const before = bank.polls;
      let waited = 0;
      while (bank.polls === before) { await vi.advanceTimersByTimeAsync(1000); waited += 1000; }
      gaps.push(waited);
    }
    expect(gaps).toEqual([5000, 10000, 20000, 40000, 80000, 120000, 120000]);
    bank.fail = null;
    await vi.advanceTimersByTimeAsync(120_000);
    const after = bank.polls;
    await vi.advanceTimersByTimeAsync(30_000);
    expect(bank.polls).toBe(after + 1);
    poller.stop();
    await vi.advanceTimersByTimeAsync(300_000);
    expect(bank.polls).toBe(after + 1);
  });

  it('stops the timer after an error that waiting will not fix, but still polls on focus', async () => {
    vi.useFakeTimers();
    const bank = fakeBank();
    const poller = new Poller(bank, () => {}, (e) => e instanceof ApiError && e.code === 'email_not_verified');
    poller.start();
    bank.fail = new ApiError(403, 'email_not_verified', 'x');
    await vi.advanceTimersByTimeAsync(30_000);
    expect(bank.polls).toBe(1);
    await vi.advanceTimersByTimeAsync(600_000);
    expect(bank.polls).toBe(1);
    bank.fail = null;
    document.dispatchEvent(new Event('visibilitychange'));
    await vi.advanceTimersByTimeAsync(0);
    expect(bank.polls).toBe(2);
    await vi.advanceTimersByTimeAsync(30_000);
    expect(bank.polls).toBe(3);
    poller.stop();
  });

  it('polls when the tab becomes visible', async () => {
    const bank = fakeBank();
    const poller = new Poller(bank, () => {});
    poller.start();
    document.dispatchEvent(new Event('visibilitychange'));
    await vi.waitFor(() => expect(bank.polls).toBe(1));
    poller.stop();
  });

  it('a nudge during a poll runs exactly one more poll after it', async () => {
    const bank = fakeBank();
    const poller = new Poller(bank, () => {});
    bank.gate = gate();
    const running = poller.poll();
    poller.nudge(); poller.nudge(); poller.nudge();
    const g = bank.gate;
    bank.gate = null;
    g.open();
    await running;
    await vi.waitFor(() => expect(bank.polls).toBe(2));
    await new Promise((r) => setTimeout(r, 10));
    expect(bank.polls).toBe(2);
  });

  it('a nudge with nothing running polls at once', async () => {
    const bank = fakeBank();
    const poller = new Poller(bank, () => {});
    poller.nudge();
    await vi.waitFor(() => expect(bank.polls).toBe(1));
  });

  it('polls every 2 minutes while the live stream is connected, and every 30 s again once it is not', async () => {
    vi.useFakeTimers();
    const bank = fakeBank();
    const poller = new Poller(bank, () => {});
    poller.start();
    poller.setLive(true);
    await vi.advanceTimersByTimeAsync(119_000);
    expect(bank.polls).toBe(0);
    await vi.advanceTimersByTimeAsync(1_000);
    expect(bank.polls).toBe(1);
    poller.setLive(false);
    await vi.advanceTimersByTimeAsync(30_000);
    expect(bank.polls).toBe(2);
    poller.stop();
  });
});
