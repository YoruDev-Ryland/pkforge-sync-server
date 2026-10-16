import { api, friendlyError } from './api';
import type { AdminCreateUserResponse, AdminPasswordResponse, AdminSettings, AdminUser, AdminUsersResponse } from './protocol';

type Init = { method?: string; json?: unknown };
export type AdminCall = <T>(path: string, init?: Init) => Promise<T>;

const user = (id: string) => `/v1/admin/users/${encodeURIComponent(id)}`;

/**
 * The server admin tools on the Account page: the sign-ups switch and the users list. Every change refetches the
 * list, so counts and roles are what the server holds.
 */
export class AdminTools {
  users = $state.raw<AdminUser[]>([]);
  total = $state(0);
  allowRegistration = $state<boolean | null>(null);
  loaded = $state(false);
  error = $state<string | null>(null);
  #generation = 0;

  constructor(private call: AdminCall = api) {}

  async load(): Promise<void> {
    const generation = this.#generation;
    try {
      const [settings, list] = await Promise.all([
        this.call<AdminSettings>('/v1/admin/settings'),
        this.call<AdminUsersResponse>('/v1/admin/users'),
      ]);
      if (generation !== this.#generation) return;
      this.allowRegistration = settings.allowRegistration;
      this.users = list.users;
      this.total = list.total;
      this.loaded = true;
      this.error = null;
    } catch (e) {
      if (generation === this.#generation) this.error = friendlyError(e);
    }
  }

  async setAllowRegistration(allow: boolean): Promise<void> {
    const before = this.allowRegistration;
    this.allowRegistration = allow;
    try {
      this.allowRegistration = (await this.call<AdminSettings>('/v1/admin/settings', { method: 'PUT', json: { allowRegistration: allow } })).allowRegistration;
    } catch (e) {
      this.allowRegistration = before;
      throw e;
    }
  }

  /** Returns the new account's password: the only copy. */
  async addUser(email: string, isAdmin: boolean): Promise<AdminCreateUserResponse> {
    const r = await this.call<AdminCreateUserResponse>('/v1/admin/users', { json: { email: email.trim(), isAdmin } });
    void this.load();
    return r;
  }

  /** Returns the new password: the only copy. */
  async resetPassword(id: string, signOutDevices: boolean): Promise<string> {
    const r = await this.call<AdminPasswordResponse>(`${user(id)}/reset-password`, { json: { signOutDevices } });
    void this.load();
    return r.password;
  }

  async setAdmin(id: string, isAdmin: boolean): Promise<void> {
    await this.call(`${user(id)}/admin`, { method: 'PUT', json: { isAdmin } });
    await this.load();
  }

  async signOutDevices(id: string): Promise<void> {
    await this.call(`${user(id)}/sign-out`, { method: 'POST' });
    await this.load();
  }

  async deleteUser(id: string, email: string): Promise<void> {
    await this.call(user(id), { method: 'DELETE', json: { email } });
    await this.load();
  }

  /** Signing out: forget what this admin saw. */
  reset(): void {
    this.#generation++;
    this.users = []; this.total = 0; this.allowRegistration = null; this.loaded = false; this.error = null;
  }
}

export const admin = new AdminTools();

/** Deleting an account is confirmed by typing its email (any case, spaces around it ignored). */
export const confirmsEmail = (typed: string, email: string) => typed.trim().toLowerCase() === email.toLowerCase();

/** Shown under the list when the server sent fewer accounts than it has. */
export function capNote(total: number, shown: number): string | null {
  return total > shown ? `Showing the newest ${shown} of ${total} accounts.` : null;
}

/** "1 device", "3 Pokémon": the phone layout's counts. */
export const plural = (n: number, one: string, many = `${one}s`) => `${n} ${n === 1 ? one : many}`;
