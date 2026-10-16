import { describe, it, expect } from 'vitest';
import cases from './fixtures/sprite-cases.json';
import { spriteCandidates } from './sprites';

describe('sprites', () => {
  it.each(cases.map((c) => [JSON.stringify(c.info), c] as const))('%s matches the app', (_, c) => {
    expect(spriteCandidates({ ...c.info, nickname: '', level: 1, generation: 8, sourceName: '' })).toEqual(c.candidates);
  });
  it('treats entries without traits as male, non-cosplay', () => {
    expect(spriteCandidates({ species: 521, form: 0, shiny: false, nickname: '', level: 1, generation: 5, sourceName: '' })[0]).toBe('/pkhex/sprites/b_521.png');
  });
});
