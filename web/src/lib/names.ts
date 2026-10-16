import { createSubscriber } from 'svelte/reactivity';
import { api } from './api';

interface NameTables { species: string[]; items: string[]; moves: string[]; abilities: string[]; natures: string[]; types: string[]; balls: string[] }

/** English names from the server's PKHeX tables. Lookups are reactive: they update once the tables arrive. */
class Names {
  #tables: NameTables | null = null;
  #changed: () => void = () => {};
  #subscribe = createSubscriber((update) => { this.#changed = update; return () => { this.#changed = () => {}; }; });

  get loaded(): boolean { this.#subscribe(); return this.#tables !== null; }

  species(id: number): string { return this.#lookup('species', id) || `#${String(id).padStart(3, '0')}`; }
  item(id: number): string { return this.#lookup('items', id); }
  /** The species' name, or '' while the tables are loading or for an unknown number. */
  speciesName(id: number): string { return this.#lookup('species', id); }
  /** The highest National Dex number the tables know (index 0 is not a species), or null until they load. */
  get maxSpecies(): number | null { this.#subscribe(); return this.#tables ? Math.max(0, this.#tables.species.length - 1) : null; }

  #loading: Promise<void> | null = null;

  /** Loads the tables once; a failed load can be retried by calling again. */
  load(): Promise<void> {
    if (this.#tables) return Promise.resolve();
    return (this.#loading ??= api<NameTables>('/v1/web/names')
      .then((tables) => { this.#tables = tables; this.#changed(); })
      .finally(() => { this.#loading = null; }));
  }

  #lookup(table: keyof NameTables, id: number): string {
    this.#subscribe();
    return this.#tables?.[table]?.[id] ?? '';
  }
}

export const names = new Names();
