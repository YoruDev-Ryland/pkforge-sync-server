const SHOWN_MS = 2600;

/** One toast at a time; a new one replaces the current and restarts the clock. */
class Toasts {
  message = $state<string | null>(null);
  /** Bumped per toast so the same text twice still replays the entrance. */
  seq = $state(0);
  #timer: ReturnType<typeof setTimeout> | undefined;

  show(message: string): void {
    this.message = message;
    this.seq++;
    clearTimeout(this.#timer);
    this.#timer = setTimeout(() => { this.message = null; }, SHOWN_MS);
  }
}

export const toasts = new Toasts();
export const toast = (message: string) => toasts.show(message);
