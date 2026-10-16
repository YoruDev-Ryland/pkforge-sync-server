import { describe, expect, it } from 'vitest';
import { dexNo, misplaced, offSlotNote, rangeText, regionRange, slotLabel, slotSpecies } from './livingdex';

const MAX = 1025;

describe('regionRange', () => {
  it('covers #1–30 in the start box and the next 30 in each box after it', () => {
    expect(regionRange(9, 9, MAX)).toEqual({ first: 1, last: 30 });
    expect(regionRange(9, 17, MAX)).toEqual({ first: 241, last: 270 });
    expect(regionRange(1, 2, MAX)).toEqual({ first: 31, last: 60 });
  });
  it('caps the last, partial box at the highest species', () => {
    expect(regionRange(0, 34, MAX)).toEqual({ first: 1021, last: 1025 });
    expect(regionRange(0, 34, 1022)).toEqual({ first: 1021, last: 1022 });
  });
  it('is null outside the region', () => {
    expect(regionRange(9, 8, MAX)).toBeNull(); // before the start
    expect(regionRange(9, 9 + 35, MAX)).toBeNull(); // after the last box
    expect(regionRange(0, 34, 1020)).toBeNull(); // past the highest species
    expect(regionRange(null, 3, MAX)).toBeNull();
    expect(regionRange(undefined, 3, MAX)).toBeNull();
    expect(regionRange(0, 0, 0)).toBeNull(); // no species known
  });
});

describe('slotSpecies', () => {
  it('maps box and slot to the National Dex number', () => {
    expect(slotSpecies(9, 9, 0, MAX)).toBe(1);
    expect(slotSpecies(9, 9, 24, MAX)).toBe(25);
    expect(slotSpecies(9, 17, 22, MAX)).toBe(263); // Zigzagoon, as in the app
    expect(slotSpecies(9, 18, 9, MAX)).toBe(280); // Ralts
    expect(slotSpecies(0, 34, 4, MAX)).toBe(1025);
  });
  it('is null past the highest species and outside the region', () => {
    expect(slotSpecies(0, 34, 5, MAX)).toBeNull();
    expect(slotSpecies(9, 8, 0, MAX)).toBeNull();
    expect(slotSpecies(null, 9, 0, MAX)).toBeNull();
  });
});

describe('misplaced', () => {
  it('flags a Pokémon of another species in a region slot', () => {
    expect(misplaced(9, 9, 24, 265, MAX)).toEqual({ expected: 25 });
  });
  it("accepts the slot's own species (any form)", () => {
    expect(misplaced(9, 9, 24, 25, MAX)).toBeNull();
  });
  it('ignores slots outside the region', () => {
    expect(misplaced(9, 8, 24, 265, MAX)).toBeNull();
    expect(misplaced(null, 9, 24, 265, MAX)).toBeNull();
    expect(misplaced(0, 34, 10, 1, MAX)).toBeNull(); // past #1025 in the last box
  });
});

describe('labels', () => {
  it('pads dex numbers to three digits, four-digit ones as is', () => {
    expect(dexNo(1)).toBe('#001');
    expect(dexNo(25)).toBe('#025');
    expect(dexNo(1025)).toBe('#1025');
    expect(rangeText({ first: 241, last: 270 })).toBe('#241–270');
    expect(rangeText({ first: 1021, last: 1025 })).toBe('#1021–1025');
  });
  it('describes slots for assistive tech', () => {
    const sp = (n: number) => ({ 25: 'Pikachu', 265: 'Wurmple' })[n] ?? '';
    expect(slotLabel(24, null, 25, sp)).toBe('Slot 25, empty, living dex #025 Pikachu');
    expect(slotLabel(24, null, 25, () => '')).toBe('Slot 25, empty, living dex #025');
    expect(slotLabel(3, null, null, sp)).toBe('Slot 4, empty');
    expect(slotLabel(0, 'LEAFY', null, sp)).toBe('Slot 1, LEAFY');
    expect(slotLabel(24, 'Wormy', 25, sp)).toBe("Slot 25, Wormy, in #025 Pikachu's living dex slot");
  });
  it('explains an off-slot Pokémon from numbers and species names only', () => {
    const sp = (n: number) => ({ 25: 'Pikachu', 265: 'Wurmple' })[n] ?? '';
    expect(offSlotNote(265, 25, sp)).toBe("#265 Wurmple is in #025 Pikachu's living dex slot. While it's here, Pikachu can't go into the living dex.");
    expect(offSlotNote(265, 25, () => '')).toBe("#265 is in #025's living dex slot. While it's here, #025 can't go into the living dex.");
  });
});
