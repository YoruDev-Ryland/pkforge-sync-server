import { describe, expect, it } from 'vitest';
import { AdminTools, capNote, confirmsEmail, plural, type AdminCall } from './admin.svelte';
import { ApiError } from './api';
import type { AdminUser } from './protocol';

const misty: AdminUser = {
  id: 'm', email: 'misty@example.com', isAdmin: false, emailVerified: true, devices: 2, mons: 40,
  createdUtc: '2026-10-01T00:00:00Z', lastActiveUtc: null,
};

/** A fake server: answers by "METHOD path", records every call. */
function server(routes: Record<string, (body: any) => unknown>) {
  const calls: { key: string; body: unknown }[] = [];
  const call: AdminCall = async <T,>(path: string, init: { method?: string; json?: unknown } = {}) => {
    const key = `${init.method ?? (init.json !== undefined ? 'POST' : 'GET')} ${path}`;
    calls.push({ key, body: init.json });
    const route = routes[key];
    if (!route) throw new Error(`unexpected ${key}`);
    return route(init.json) as T;
  };
  return { call, calls, keys: () => calls.map((c) => c.key) };
}

const listing = {
  'GET /v1/admin/settings': () => ({ allowRegistration: false }),
  'GET /v1/admin/users': () => ({ users: [misty], total: 1, limit: 500 }),
};

describe('AdminTools', () => {
  it('loads the sign-ups switch and the users list', async () => {
    const s = server(listing);
    const tools = new AdminTools(s.call);
    await tools.load();
    expect(tools.loaded).toBe(true);
    expect(tools.allowRegistration).toBe(false);
    expect(tools.users).toEqual([misty]);
    expect(tools.total).toBe(1);
  });

  it('keeps the server\'s message when loading fails', async () => {
    const tools = new AdminTools(async () => { throw new ApiError(403, 'admin_required', 'Only server admins can do this.'); });
    await tools.load();
    expect(tools.loaded).toBe(false);
    expect(tools.error).toBe('Only server admins can do this.');
  });

  it('flips the switch at once and puts it back when the server says no', async () => {
    let fail = false;
    const s = server({ ...listing, 'PUT /v1/admin/settings': (b) => { if (fail) throw new ApiError(0, 'offline', ''); return b; } });
    const tools = new AdminTools(s.call);
    await tools.load();
    await tools.setAllowRegistration(true);
    expect(tools.allowRegistration).toBe(true);
    expect(s.calls.at(-1)).toEqual({ key: 'PUT /v1/admin/settings', body: { allowRegistration: true } });
    fail = true;
    await expect(tools.setAllowRegistration(false)).rejects.toBeInstanceOf(ApiError);
    expect(tools.allowRegistration).toBe(true);
  });

  it('returns generated passwords and refetches the list after each change', async () => {
    const s = server({
      ...listing,
      'POST /v1/admin/users': (b) => ({ user: { ...misty, email: b.email, isAdmin: b.isAdmin }, password: 'p'.repeat(16) }),
      'POST /v1/admin/users/m/reset-password': () => ({ password: 'q'.repeat(16) }),
      'PUT /v1/admin/users/m/admin': () => misty,
      'POST /v1/admin/users/m/sign-out': () => undefined,
      'DELETE /v1/admin/users/m': () => undefined,
    });
    const tools = new AdminTools(s.call);
    const created = await tools.addUser('  brock@example.com ', true);
    expect(created.password).toBe('p'.repeat(16));
    expect(s.calls[0].body).toEqual({ email: 'brock@example.com', isAdmin: true });
    expect(await tools.resetPassword('m', true)).toBe('q'.repeat(16));
    expect(s.calls.find((c) => c.key.endsWith('reset-password'))!.body).toEqual({ signOutDevices: true });
    await tools.setAdmin('m', true);
    await tools.signOutDevices('m');
    await tools.deleteUser('m', 'misty@example.com');
    expect(s.calls.at(-3)).toEqual({ key: 'DELETE /v1/admin/users/m', body: { email: 'misty@example.com' } });
    expect(s.keys().filter((k) => k === 'GET /v1/admin/users').length).toBe(5);
  });

  it('forgets everything on sign-out, even a load still under way', async () => {
    let release!: () => void;
    const gate = new Promise<void>((r) => (release = r));
    const s = server({ 'GET /v1/admin/settings': async () => { await gate; return { allowRegistration: true }; }, 'GET /v1/admin/users': listing['GET /v1/admin/users'] });
    const tools = new AdminTools(s.call);
    const loading = tools.load();
    tools.reset();
    release();
    await loading;
    expect(tools.loaded).toBe(false);
    expect(tools.users).toEqual([]);
    expect(tools.allowRegistration).toBeNull();
  });
});

describe('admin helpers', () => {
  it('confirms a delete with the email in any case', () => {
    expect(confirmsEmail(' Misty@Example.com ', 'misty@example.com')).toBe(true);
    expect(confirmsEmail('misty@example', 'misty@example.com')).toBe(false);
    expect(confirmsEmail('', 'misty@example.com')).toBe(false);
  });

  it('notes a capped list only', () => {
    expect(capNote(3, 3)).toBeNull();
    expect(capNote(612, 500)).toBe('Showing the newest 500 of 612 accounts.');
  });

  it('counts in words', () => {
    expect(plural(1, 'device')).toBe('1 device');
    expect(plural(0, 'device')).toBe('0 devices');
    expect(plural(2, 'Pokémon', 'Pokémon')).toBe('2 Pokémon');
  });
});
