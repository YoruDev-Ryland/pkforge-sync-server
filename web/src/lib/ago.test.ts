import { describe, it, expect } from 'vitest';
import { ago, countdown } from './ago';

const now = new Date(2026, 9, 15, 14, 30, 0);
const at = (y: number, mo: number, d: number, h = 12, mi = 0) => new Date(y, mo - 1, d, h, mi);

describe('ago', () => {
  it('says how long since a sync, the way people do', () => {
    expect(ago(null, now)).toBe('never');
    expect(ago(new Date(now.getTime() - 20_000), now)).toBe('just now');
    expect(ago(new Date(now.getTime() - 2 * 60_000), now)).toBe('2 min ago');
    expect(ago(at(2026, 10, 15, 9, 10), now)).toBe('5 h ago');
    expect(ago(at(2026, 10, 14, 23, 50), now)).toBe('yesterday');
    expect(ago(at(2026, 10, 3), now)).toBe('Oct 3');
    expect(ago(at(2025, 12, 30), now)).toBe('Dec 30, 2025');
  });
  it('counts a code down in minutes and seconds', () => {
    expect(countdown(292_400)).toBe('4:52');
    expect(countdown(5_000)).toBe('0:05');
    expect(countdown(-1)).toBe('0:00');
  });
});
