// The app's Living Dex Autopilot lays the Bank's living dex out in National Dex order, 30 per box, from the
// `livingDexStartBox` setting: species N lives in box start + floor((N - 1) / 30), slot (N - 1) % 30.
// It never displaces a Pokémon, so another species sitting in a region slot keeps that slot's species out.

export const PER_BOX = 30;

/** Used until the name tables (whose species list gives the real number) have loaded. */
export const FALLBACK_MAX_SPECIES = 1025;

export interface DexRange { first: number; last: number }

/** The National Dex numbers a box holds in the living dex (the last box capped at `max`), or null outside it. */
export function regionRange(start: number | null | undefined, box: number, max: number): DexRange | null {
  if (start == null || start < 0 || box < start) return null;
  const first = (box - start) * PER_BOX + 1;
  if (first > max) return null;
  return { first, last: Math.min(first + PER_BOX - 1, max) };
}

/** The species a living dex slot is for, or null when the slot isn't one. */
export function slotSpecies(start: number | null | undefined, box: number, slot: number, max: number): number | null {
  const range = regionRange(start, box, max);
  if (!range) return null;
  const n = range.first + slot;
  return n <= range.last ? n : null;
}

/** A Pokémon of another species in a living dex slot (forms of the slot's species belong there). */
export function misplaced(start: number | null | undefined, box: number, slot: number, species: number, max: number): { expected: number } | null {
  const expected = slotSpecies(start, box, slot, max);
  return expected !== null && expected !== species ? { expected } : null;
}

/** "#025", "#1025". */
export const dexNo = (n: number) => `#${String(n).padStart(3, '0')}`;

/** "#241–270". */
export const rangeText = (r: DexRange) => `${dexNo(r.first)}–${String(r.last).padStart(3, '0')}`;

const named = (n: number, species: (n: number) => string) => [dexNo(n), species(n)].filter(Boolean).join(' ');

/**
 * A slot's accessible name. `dex` is the living dex number worth mentioning: the slot's species when it is empty,
 * the species it is kept from when another Pokémon sits there, else null.
 */
export function slotLabel(slot: number, nickname: string | null, dex: number | null, species: (n: number) => string): string {
  const base = `Slot ${slot + 1}, ${nickname ?? 'empty'}`;
  if (dex === null) return base;
  return nickname === null ? `${base}, living dex ${named(dex, species)}` : `${base}, in ${named(dex, species)}'s living dex slot`;
}

/** Why an off-slot Pokémon matters. Built from numbers and the species table only, never from nicknames. */
export function offSlotNote(species: number, expected: number, name: (n: number) => string): string {
  const blocked = name(expected) || dexNo(expected);
  return `${named(species, name)} is in ${named(expected, name)}'s living dex slot. While it's here, ${blocked} can't go into the living dex.`;
}
