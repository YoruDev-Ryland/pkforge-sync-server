import { describe, it, expect } from 'vitest';
import { radarPoints } from './Radar.svelte';

describe('radarPoints', () => {
  it('puts the first point straight up and goes clockwise', () => {
    const [[x0, y0], [x1, y1]] = radarPoints([100, 100, 100, 100, 100, 100], 100, 130, 119, 74);
    expect(x0).toBeCloseTo(130); expect(y0).toBeCloseTo(45);
    expect(x1).toBeCloseTo(130 + 74 * Math.cos(-Math.PI / 6)); expect(y1).toBeCloseTo(119 - 37);
  });
  it('clamps to the grid, and keeps zero a little off the center', () => {
    const pts = radarPoints([0, 500], 100, 0, 0, 100);
    expect(pts[0][1]).toBeCloseTo(-4);
    expect(pts[1][1]).toBeCloseTo(100);
  });
});
