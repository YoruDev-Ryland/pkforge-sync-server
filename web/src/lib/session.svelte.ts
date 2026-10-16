import { api, ApiError } from './api';
import type { AccountInfo, DeviceInfo, RegisterRequest, ServerInfo, WebSessionRequest, WebSessionResponse } from './protocol';

export type SessionState = 'unknown' | 'signedOut' | 'signedIn';

/** Whether this browser holds a working device cookie, plus what the server says about itself. */
class Session {
  state = $state<SessionState>('unknown');
  signedOutReason = $state<string | null>(null);
  info = $state<ServerInfo | null>(null);
  devices = $state<DeviceInfo[]>([]);
  /** The signed-in account (email, admin); null until loaded. */
  account = $state<AccountInfo | null>(null);
  /** Set when the first check couldn't reach the server; the page offers a retry. */
  startError = $state<ApiError | null>(null);
  /** Our own sign-out is under way: the server's device-removed (or a 401) for it is no news. */
  #signingOut = false;

  async start(): Promise<void> {
    this.startError = null;
    try {
      this.info ??= await api<ServerInfo>('/v1/server-info');
      [this.devices, this.account] = await Promise.all([api<DeviceInfo[]>('/v1/devices'), api<AccountInfo>('/v1/account')]);
      this.state = 'signedIn';
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) this.state = 'signedOut';
      else this.startError = e instanceof ApiError ? e : new ApiError(0, 'offline', String(e));
    }
  }

  async signIn(req: WebSessionRequest): Promise<void> {
    await api<WebSessionResponse>('/v1/web/session', { json: req });
    this.#opened();
  }

  /** Creates an account with this browser as its first device, signed in like a password sign-in. */
  async register(req: RegisterRequest): Promise<void> {
    await api<WebSessionResponse>('/v1/web/register', { json: req });
    this.#opened();
  }

  #opened(): void {
    this.signedOutReason = null;
    this.state = 'signedIn';
    void this.refreshDevices();
    void this.refreshAccount();
  }

  /** A 401 means this device was already removed elsewhere: the browser is signed out either way. */
  async signOut(): Promise<void> {
    this.#signingOut = true;
    try { await api('/v1/web/session', { method: 'DELETE' }); }
    catch (e) { if (!(e instanceof ApiError && e.status === 401)) throw e; }
    finally { this.#signingOut = false; }
    this.signedOutReason = null;
    this.devices = [];
    this.account = null;
    this.state = 'signedOut';
  }

  expire(reason: string): void {
    if (this.state === 'signedOut' || this.#signingOut) return;
    this.signedOutReason = reason;
    this.devices = [];
    this.account = null;
    this.state = 'signedOut';
  }

  async refreshDevices(): Promise<void> {
    try { this.devices = await api<DeviceInfo[]>('/v1/devices'); } catch { /* the poller reports trouble */ }
  }

  /** Sign-ups may have been opened or closed since the page loaded. */
  async refreshInfo(): Promise<void> {
    try { this.info = await api<ServerInfo>('/v1/server-info'); } catch { /* keep what we have */ }
  }

  async refreshAccount(): Promise<void> {
    try { this.account = await api<AccountInfo>('/v1/account'); } catch { /* the poller reports trouble */ }
  }
}

export const session = new Session();
