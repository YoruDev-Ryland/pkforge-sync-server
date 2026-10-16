const tabHidden = () => typeof document !== 'undefined' && document.visibilityState === 'hidden';

/**
 * Whether to tell the user the server can't be reached. One failed call is only a suspicion (a blip, a restart);
 * the banner waits for a second failure in a row, e.g. the Poller's retry 5 s later or the live stream's next
 * reconnect. A push that ran out of retries is confirmed at once. Failures while the tab is hidden don't count
 * (browsers throttle and cut background tabs), and any success clears everything.
 */
export class Connectivity {
  offline = $state(false);
  #suspect = false;

  constructor(private hidden: () => boolean = tabHidden) {}

  /** The server answered. */
  ok(): void {
    this.#suspect = false;
    this.offline = false;
  }

  /** A call couldn't reach the server (or got a 5xx): the second in a row means offline. */
  failed(): void {
    if (this.hidden()) return;
    if (this.#suspect) this.offline = true;
    this.#suspect = true;
  }

  /** A failure that is already confirmed (a push that used up its retries). */
  lost(): void {
    if (this.hidden()) return;
    this.#suspect = true;
    this.offline = true;
  }

  reset(): void {
    this.#suspect = false;
    this.offline = false;
  }
}
