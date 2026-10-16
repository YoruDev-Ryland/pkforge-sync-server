import { describe, it, expect, vi, beforeEach } from 'vitest';
import { api, ApiError, friendlyError } from './api';

describe('api', () => {
  beforeEach(() => vi.restoreAllMocks());

  it('sends the protocol header and same-origin credentials', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('{"ok":1}', { status: 200, headers: { 'content-type': 'application/json' } }));
    await api('/v1/devices');
    const [, init] = fetchMock.mock.calls[0];
    expect(new Headers(init!.headers).get('X-PKForge-Protocol')).toBe('1');
    expect(init!.credentials).toBe('same-origin');
  });

  it('turns ErrorResponse bodies into ApiError', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('{"code":"invalid_credentials","message":"nope"}', { status: 401 }));
    await expect(api('/v1/web/session', { method: 'POST', json: {} })).rejects.toMatchObject({ status: 401, code: 'invalid_credentials' });
  });

  it('maps network failures to offline', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'));
    await expect(api('/v1/devices')).rejects.toMatchObject({ status: 0, code: 'offline' });
  });

  it('returns undefined for 204', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 204 }));
    expect(await api('/v1/web/session', { method: 'DELETE' })).toBeUndefined();
  });

  it('words errors for people', () => {
    expect(friendlyError(new ApiError(401, 'invalid_credentials', 'x'))).toBe('Email or password is wrong.');
    expect(friendlyError(new ApiError(429, 'rate_limited', 'x'))).toBe('Too many tries. Wait a minute and try again.');
    expect(friendlyError(new ApiError(400, 'invalid_pairing_code', 'x'))).toBe('That code expired or was already used. Make a new one in the app.');
    expect(friendlyError(new ApiError(0, 'offline', 'x'))).toBe("Can't reach the server. Check your connection and try again.");
  });

  it('shows the server reason for writes_disabled, without repeating its default', () => {
    expect(friendlyError(new ApiError(403, 'writes_disabled', 'Storage quota reached.')))
      .toBe("This server isn't accepting changes for your account: Storage quota reached.");
    expect(friendlyError(new ApiError(403, 'writes_disabled', "This server isn't accepting changes for this account.")))
      .toBe("This server isn't accepting changes for your account.");
    expect(friendlyError(new ApiError(403, 'writes_disabled', ''))).toBe("This server isn't accepting changes for your account.");
  });
});

describe('onApiError', () => {
  beforeEach(() => vi.restoreAllMocks());

  it('tells listeners about every failed call, e.g. 426 from a new server build', async () => {
    const { onApiError } = await import('./api');
    const seen: ApiError[] = [];
    const off = onApiError((e) => seen.push(e));
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('{"code":"unsupported_protocol","message":"old"}', { status: 426 }));
    await expect(api('/v1/devices')).rejects.toBeInstanceOf(ApiError);
    off();
    await expect(api('/v1/devices')).rejects.toBeInstanceOf(ApiError);
    expect(seen.map((e) => [e.status, e.code])).toEqual([[426, 'unsupported_protocol']]);
  });
});
