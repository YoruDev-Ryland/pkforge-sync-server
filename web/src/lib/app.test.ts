import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { app, names, session, SIGNED_OUT_ELSEWHERE } from './app.svelte';
import { toasts } from './toast.svelte';

type Route = (method: string) => { status: number; body?: unknown } | Response;
let routes: Record<string, Route> = {};
const calls: string[] = [];

const changes = { epoch: 'e1', next: 1, hasMore: false, items: [] };

beforeEach(() => {
  calls.length = 0;
  routes = {};
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const method = init?.method ?? 'GET';
    const path = url.split('?')[0];
    calls.push(`${method} ${path}`);
    const route = routes[path];
    const answer = route ? route(method) : { status: 404, body: { code: 'not_found', message: 'x' } };
    if (answer instanceof Response) return answer;
    const { status, body } = answer;
    return new Response(status === 204 ? null : JSON.stringify(body ?? {}), { status, headers: { 'content-type': 'application/json' } });
  }));
});
afterEach(() => { app.reset(); vi.unstubAllGlobals(); });

describe('unverified email', () => {
  it('shows the verify banner when changes are refused, resends the link, and clears once the Bank loads', async () => {
    routes['/v1/sync/changes'] = () => ({ status: 403, body: { code: 'email_not_verified', message: 'Verify your email.' } });
    routes['/v1/auth/resend-verification'] = () => ({ status: 204 });
    await app.loadBank();
    expect(app.unverified).toBe(true);
    expect(app.offline).toBe(false);

    await app.resendVerification();
    expect(calls).toContain('POST /v1/auth/resend-verification');
    expect(toasts.message).toBe('Sent. Check your inbox for the link.');

    routes['/v1/sync/changes'] = () => ({ status: 200, body: changes });
    await app.loadBank();
    expect(app.unverified).toBe(false);
  });
});

describe('names retry', () => {
  it('retries the name tables after the next successful poll until they load', async () => {
    routes['/v1/web/names'] = () => ({ status: 500, body: { code: 'http_500', message: 'x' } });
    await names.load().catch(() => {});
    expect(names.loaded).toBe(false);

    routes['/v1/sync/changes'] = () => ({ status: 200, body: changes });
    routes['/v1/web/names'] = () => ({ status: 200, body: { species: ['', 'Bulbasaur'], items: [], moves: [], abilities: [], natures: [], types: [], balls: [] } });
    await app.loadBank();
    await vi.waitFor(() => expect(names.loaded).toBe(true));
    expect(names.species(1)).toBe('Bulbasaur');

    const before = calls.filter((c) => c.endsWith('/v1/web/names')).length;
    await app.poll();
    expect(calls.filter((c) => c.endsWith('/v1/web/names')).length).toBe(before);
  });
});

/** A live event stream the test writes to. */
function liveRoute() {
  let ctrl!: ReadableStreamDefaultController<Uint8Array>;
  const enc = new TextEncoder();
  routes['/v1/sync/events'] = () => new Response(new ReadableStream<Uint8Array>({ start: (c) => { ctrl = c; } }),
    { status: 200, headers: { 'content-type': 'text/event-stream' } });
  return { send: (event: string, data = '{}') => ctrl.enqueue(enc.encode(`event: ${event}\ndata: ${data}\n\n`)) };
}
const count = (call: string) => calls.filter((c) => c === call).length;

describe('live updates', () => {
  afterEach(() => { session.state = 'unknown'; session.signedOutReason = null; });

  it('catches up on connect, polls on bank, refreshes devices and theme, and lets changes retry on account', async () => {
    routes['/v1/sync/changes'] = () => ({ status: 200, body: changes });
    routes['/v1/devices'] = () => ({ status: 200, body: [] });
    routes['/v1/account/preferences'] = () => ({ status: 200, body: {} });
    const live = liveRoute();
    await app.loadBank();
    app.startPolling();
    await vi.waitFor(() => expect(count('GET /v1/sync/events')).toBe(1));
    expect(app.live).toBe(false);

    const before = count('GET /v1/sync/changes');
    live.send('ready', '{"version":1}');
    await vi.waitFor(() => expect(app.live).toBe(true));
    await vi.waitFor(() => expect(count('GET /v1/sync/changes')).toBe(before + 1));

    live.send('bank', '{"version":2}');
    await vi.waitFor(() => expect(count('GET /v1/sync/changes')).toBe(before + 2));
    live.send('devices');
    await vi.waitFor(() => expect(count('GET /v1/devices')).toBe(1));
    live.send('prefs');
    await vi.waitFor(() => expect(count('GET /v1/account/preferences')).toBe(1));
    app.sync.readOnly = true;
    const beforeAccount = count('GET /v1/sync/changes');
    live.send('account');
    await vi.waitFor(() => expect(app.readOnly).toBe(false));
    await vi.waitFor(() => expect(count('GET /v1/sync/changes')).toBe(beforeAccount + 1));

    app.stopPolling();
    expect(app.live).toBe(false);
  });

  it('signs out when this browser is removed elsewhere', async () => {
    session.state = 'signedIn';
    routes['/v1/sync/changes'] = () => ({ status: 200, body: changes });
    const live = liveRoute();
    app.startPolling();
    await vi.waitFor(() => expect(count('GET /v1/sync/events')).toBe(1));
    live.send('ready', '{"version":1}');
    live.send('device-removed');
    await vi.waitFor(() => expect(session.state).toBe('signedOut'));
    expect(session.signedOutReason).toBe(SIGNED_OUT_ELSEWHERE);
  });
});

describe('offline banner', () => {
  it('a single failed poll is not enough; a second in a row is, and a good one clears it', async () => {
    routes['/v1/sync/changes'] = () => ({ status: 503, body: { code: 'http_503', message: 'x' } });
    await app.poll();
    expect(app.offline).toBe(false);
    await app.poll();
    expect(app.offline).toBe(true);
    routes['/v1/sync/changes'] = () => ({ status: 200, body: changes });
    await app.poll();
    expect(app.offline).toBe(false);
  });

  it('a network failure followed by a success never shows it', async () => {
    let fail = true;
    vi.stubGlobal('fetch', vi.fn(async (url: string) => {
      calls.push(`GET ${url.split('?')[0]}`);
      if (fail) throw new TypeError('network');
      return new Response(JSON.stringify(changes), { status: 200, headers: { 'content-type': 'application/json' } });
    }));
    await app.poll();
    expect(app.offline).toBe(false);
    fail = false;
    await app.poll();
    fail = true;
    await app.poll();
    expect(app.offline).toBe(false);
  });
});

describe('own sign-out', () => {
  afterEach(() => { session.state = 'unknown'; session.signedOutReason = null; });

  it('never says "signed out from another device" while signing out here', async () => {
    session.state = 'signedIn';
    let answer!: () => void;
    const deleted = new Promise<void>((r) => (answer = r));
    vi.stubGlobal('fetch', vi.fn(async () => { await deleted; return new Response(null, { status: 204 }); }));
    const out = session.signOut();
    // The server's device-removed (or a 401 elsewhere) arrives before the DELETE answers.
    session.expire(SIGNED_OUT_ELSEWHERE);
    expect(session.signedOutReason).toBe(null);
    expect(session.state).toBe('signedIn');
    answer();
    await out;
    expect(session.state).toBe('signedOut');
    expect(session.signedOutReason).toBe(null);
  });
});
