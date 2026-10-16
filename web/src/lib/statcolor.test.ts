import { describe, it, expect } from 'vitest';
import { judge, plainStats, statColor } from './statcolor';

describe('statColor and judge (stat coloring)', () => {
  it('colors base stats from red to teal', () => {
    expect(statColor(49)).toBe('#e2583e');
    expect(statColor(69)).toBe('#e8973a');
    expect(statColor(89)).toBe('#e3c33c');
    expect(statColor(109)).toBe('#9ccf3f');
    expect(statColor(120)).toBe('#3fbf6a');
    expect(statColor(130)).toBe('#26b7c9');
  });
  it('judges IVs like the games', () => {
    expect(judge(31)).toBe('Best');
    expect(judge(30)).toBe('Fantastic');
    expect(judge(26)).toBe('Very good');
    expect(judge(16)).toBe('Pretty good');
    expect(judge(1)).toBe('Decent');
    expect(judge(0)).toBe('No good');
  });
  it('computes the species at a level with no IVs, EVs or nature', () => {
    // Sceptile at 62, HP first.
    expect(plainStats([70, 85, 65, 105, 85, 120], 62)).toEqual([158, 110, 85, 135, 110, 153]);
  });
});

import { typeDark, calendarDate, ribbonColor } from './statcolor';
describe('type badges stay readable', () => {
  it('puts dark ink on the light type colors only', () => {
    expect(['Electric', 'Ice', 'Fairy', 'Flying'].every(typeDark)).toBe(true);
    expect(['Grass', 'Fire', 'Water', 'Dragon', 'Dark', 'Normal', 'Ground'].some(typeDark)).toBe(false);
  });
  it('formats calendar dates without a time zone shift', () => {
    expect(calendarDate('2026-03-03')).toBe('Mar 3, 2026');
    expect(calendarDate(null)).toBeNull();
  });
  it('colors ribbons by the words in their names', () => {
    expect(ribbonColor('Champion G3')).toBe('#d8a020');
    expect(ribbonColor('G3 Cool Super')).toBe('#d9473f');
    expect(ribbonColor('Something Else')).toMatch(/^#[0-9a-f]{6}$/);
  });
});
