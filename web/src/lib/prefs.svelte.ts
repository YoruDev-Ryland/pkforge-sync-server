import { api } from './api';
import { applyTheme, BOX_THEME, resolveTheme, THEMES, type Mode, type ThemeId } from './theme';
import { toast } from './toast.svelte';

const MODES: Mode[] = ['system', 'light', 'dark'];
type Choice = { mode: Mode; theme: string };
const validChoice = (t: unknown): t is string => t === BOX_THEME || THEMES.some((x) => x[0] === t);

/** Appearance, saved to the account and cached in this browser so pages open in the right colors. */
class Prefs {
  mode = $state<Mode>('system');
  /** A theme id, or "box" (Match box, the default) to follow the shown box's wallpaper. */
  theme = $state<string>(BOX_THEME);
  /** The shown box's theme, which Match box uses. */
  boxTheme = $state<ThemeId>('default');
  #saved: Record<string, unknown> = {};
  #touched = false;
  #dark = typeof matchMedia === 'function' ? matchMedia('(prefers-color-scheme: dark)') : null;

  constructor() {
    // Start from what index.html already applied from the cache.
    const root = document.documentElement;
    if (root.dataset.mode === 'light' || root.dataset.mode === 'dark') this.mode = root.dataset.mode;
    const hue = root.style.getPropertyValue('--hue').trim().toLowerCase();
    const shown = THEMES.find((t) => t[2] === hue)?.[0] ?? 'default';
    let cached: unknown;
    try { cached = JSON.parse(localStorage.getItem('pkf-theme') || 'null')?.theme; } catch { /* private mode */ }
    // Older caches hold only the color, which was always a fixed choice.
    this.theme = validChoice(cached) ? cached : hue ? shown : BOX_THEME;
    this.boxTheme = shown;
    this.#dark?.addEventListener('change', () => this.#apply());
    this.#apply();
  }

  async load(): Promise<void> {
    this.#touched = false;
    const saved = (await api<Record<string, unknown>>('/v1/account/preferences')) ?? {};
    this.#saved = saved;
    if (this.#touched) return; // a choice made while loading wins
    if (MODES.includes(saved.mode as Mode)) this.mode = saved.mode as Mode;
    this.theme = validChoice(saved.theme) ? saved.theme : BOX_THEME; // no stored theme: Match box
    this.#apply();
  }

  /** Applies at once; a failed save keeps the choice in this browser and says so. */
  async set(p: Partial<Choice>): Promise<void> {
    this.#touched = true;
    if (p.mode) this.mode = p.mode;
    if (p.theme) this.theme = p.theme;
    this.#apply();
    this.#saved = { ...this.#saved, mode: this.mode, theme: this.theme };
    try { await api('/v1/account/preferences', { method: 'PUT', json: this.#saved }); }
    catch { toast("Couldn't save the theme to your account. It's kept in this browser."); }
  }

  /** Match box follows this: the shown box's theme, or Default where no box is shown (sign-in). */
  follow(theme: ThemeId): void {
    if (theme === this.boxTheme) return;
    this.boxTheme = theme;
    if (this.theme === BOX_THEME) this.#apply();
  }

  #apply(): void {
    applyTheme(document.documentElement, this.mode, resolveTheme(this.theme, this.boxTheme), this.#dark?.matches ?? false, this.theme);
  }
}

export const prefs = new Prefs();
