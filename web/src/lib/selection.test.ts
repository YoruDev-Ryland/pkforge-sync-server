import { describe, it, expect } from 'vitest';
import { select } from './selection';

const occ = (filled: number[]) => (s: number) => filled.includes(s);

describe('select', () => {
  it('plain click selects one, even an empty slot', () => {
    expect(select(new Set([1, 2]), 1, 7, occ([]), { shift: false, ctrl: false })).toEqual({ selected: new Set([7]), anchor: 7 });
  });
  it('shift selects the occupied range from the anchor', () => {
    expect(select(new Set([1]), 1, 5, occ([1, 2, 4, 5]), { shift: true, ctrl: false }).selected).toEqual(new Set([1, 2, 4, 5]));
  });
  it('shift works backwards from the anchor and keeps the anchor', () => {
    expect(select(new Set([5]), 5, 2, occ([1, 2, 4, 5]), { shift: true, ctrl: false })).toEqual({ selected: new Set([2, 4, 5]), anchor: 5 });
  });
  it('shift onto an empty slot is a plain click', () => {
    expect(select(new Set([1]), 1, 3, occ([1, 2]), { shift: true, ctrl: false })).toEqual({ selected: new Set([3]), anchor: 3 });
  });
  it('ctrl toggles one occupied slot and never empties the selection', () => {
    expect(select(new Set([1, 2]), 1, 2, occ([1, 2]), { shift: false, ctrl: true }).selected).toEqual(new Set([1]));
    expect(select(new Set([1]), 1, 1, occ([1]), { shift: false, ctrl: true }).selected).toEqual(new Set([1]));
    expect(select(new Set([1]), 1, 9, occ([1]), { shift: false, ctrl: true }).selected).toEqual(new Set([1])); // empty slots can't be added
  });
  it('ctrl from an empty selected slot starts over with the picked one', () => {
    expect(select(new Set([3]), 3, 1, occ([1]), { shift: false, ctrl: true })).toEqual({ selected: new Set([1]), anchor: 1 });
  });
  it('never changes the set it was given', () => {
    const current = new Set([1]);
    select(current, 1, 2, occ([1, 2]), { shift: false, ctrl: true });
    expect(current).toEqual(new Set([1]));
  });
});
