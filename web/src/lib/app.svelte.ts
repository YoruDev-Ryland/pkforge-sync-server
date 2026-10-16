import { admin } from './admin.svelte';
import { api, ApiError, apiResponse, friendlyError, onApiError } from './api';
import { Bank } from './bank.svelte';
import { Connectivity } from './connectivity.svelte';
import { details } from './details.svelte';
import { LiveStream, type SseEvent } from './events';
import { names } from './names';
import { Poller } from './poller';
import { prefs } from './prefs.svelte';
import type { ChangesResponse, PushResponse } from './protocol';
import { session } from './session.svelte';
import { released } from './released.svelte';
import { Sync } from './sync.svelte';
import { toast } from './toast.svelte';

export { session } from './session.svelte';
export { prefs } from './prefs.svelte';
export { names } from './names';
export { toast } from './toast.svelte';

export const SIGNED_OUT_ELSEWHERE = 'This browser was signed out from another device.';

/** What the box view and inspector are looking at. */
export const ui = $state({ box: 0, selected: new Set<number>([0]), anchor: 0, inspectorTab: 'info' });

const isUnverified = (e: unknown) => e instanceof ApiError && e.code === 'email_not_verified';
/** No answer at all, or a 5xx: the server is down just as much either way. */
const unreachable = (e: unknown) => e instanceof ApiError && (e.status === 0 || e.status >= 500);

class App {
  outdated = $state(false);
  lastSynced = $state<Date | null>(null);
  /** The server refuses sync until the account's email is verified. */
  unverified = $state(false);
  /** The live event stream is connected: other devices' changes show up at once. */
  live = $state(false);
  #pushing = $state(0);
  readonly #net = new Connectivity();

  readonly bank = new Bank({
    changes: (since, epoch) => this.#tracked(api<ChangesResponse>(`/v1/sync/changes?since=${since}${epoch ? `&epoch=${encodeURIComponent(epoch)}` : ''}`), false),
  });
  readonly sync = new Sync(this.bank, { push: (req) => this.#tracked(api<PushResponse>('/v1/sync/push', { json: req }), true) },
    { connectivity: this.#net });
  // One failed poll is only a suspicion; the Poller's retry failing too shows the banner. An unverified email stops
  // the timer (waiting won't fix it); coming back to the tab after clicking the link polls again.
  readonly #poller = new Poller(this.bank, (e) => {
    if (unreachable(e)) this.#net.failed();
    // The name tables are fetched once at sign-in; if that failed, try again whenever the server answers.
    if (e === null && !names.loaded) names.load().catch(() => {});
  }, isUnverified);
  // Other devices' changes, sign-outs and theme changes as they happen. Every (re)connect polls once so nothing
  // that happened while disconnected is missed; the timer then slows down to a safety net.
  readonly #live = new LiveStream({
    open: (signal) => apiResponse('/v1/sync/events', { signal }),
    onOpen: () => { this.#net.ok(); this.#poller.nudge(); },
    onEvent: (e) => this.#onLive(e),
    onState: (connected) => { this.live = connected; this.#poller.setLive(connected); },
    onFailure: (e) => {
      if (unreachable(e)) this.#net.failed();
      if (e instanceof ApiError && e.status === 401) session.expire(SIGNED_OUT_ELSEWHERE);
    },
  });

  get offline(): boolean { return this.#net.offline; }
  get readOnly(): boolean { return this.sync.readOnly; }
  /** Why the server isn't accepting changes, for the banner and the status line. */
  get readOnlyMessage(): string { return this.sync.readOnlyMessage; }
  /** A change is on its way to the server (background polls don't count, so the status line stays still). */
  get syncing(): boolean { return this.#pushing > 0; }

  constructor() {
    onApiError((e) => { if (e.status === 426) this.outdated = true; });
  }

  /** Sends the verification email again. */
  async resendVerification(): Promise<void> {
    try {
      await api('/v1/auth/resend-verification', { method: 'POST' });
      toast('Sent. Check your inbox for the link.');
    } catch (e) { toast(friendlyError(e)); }
  }

  loadBank(): Promise<void> { return this.#poller.load(); }
  poll(): Promise<void> { return this.#poller.poll(); }
  /** Keeps the Bank current: the live stream plus the timer and focus polls. */
  startPolling(): void { this.#poller.start(); this.#live.start(); }
  stopPolling(): void { this.#live.stop(); this.#poller.stop(); }

  /** Signing out: forget this account's Bank and view. */
  reset(): void {
    this.stopPolling();
    this.bank.reset();
    details.clear();
    released.reset();
    admin.reset();
    this.#net.reset();
    this.sync.readOnly = false;
    this.lastSynced = null;
    this.unverified = false;
    Object.assign(ui, { box: 0, selected: new Set([0]), anchor: 0 });
  }

  /** Every sync call: drives the status line, and a 401 means this device was removed elsewhere. */
  async #tracked<T>(call: Promise<T>, push: boolean): Promise<T> {
    if (push) this.#pushing++;
    try {
      const result = await call;
      this.lastSynced = new Date();
      this.#net.ok();
      this.unverified = false;
      return result;
    } catch (e) {
      // Failures count toward offline where they end: a poll in the Poller, a push once its retries are used up.
      if (isUnverified(e)) this.unverified = true;
      if (e instanceof ApiError && e.status === 401) session.expire(SIGNED_OUT_ELSEWHERE);
      throw e;
    } finally { if (push) this.#pushing--; }
  }

  #onLive(e: SseEvent): void {
    switch (e.event) {
      case 'bank': this.#poller.nudge(); break;
      case 'devices': void session.refreshDevices(); break;
      case 'device-removed': session.expire(SIGNED_OUT_ELSEWHERE); break;
      case 'prefs': prefs.load().catch(() => {}); break;
      // The account's standing changed (e.g. the server accepts changes again): re-enable controls and refetch.
      case 'account': this.sync.allowRetry(); this.#poller.nudge(); break;
    }
  }
}

export const app = new App();
export const bank = app.bank;
export const sync = app.sync;
