import { describe, it, expect } from 'vitest';
import { THEMES, inkFor } from './theme';

describe('theme', () => {
  it('has the app’s 19 themes, Default first', () => {
    expect(THEMES).toHaveLength(19);
    expect(THEMES[0]).toEqual(['default', 'Default', '#2789cd']);
    expect(THEMES.map((t) => t[0])).toContain('fairy');
  });
  it('puts dark ink on light accents and white on dark ones', () => {
    expect(inkFor('#dfc900', false)).toBe('#141414'); // Electric
    expect(inkFor('#f8a9f0', true)).toBe('#141414');  // Fairy
    expect(inkFor('#2789cd', false)).toBe('#ffffff'); // Default
    expect(inkFor('#404040', true)).toBe('#ffffff');  // Dark
  });
});
