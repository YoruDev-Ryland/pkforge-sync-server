import { describe, it, expect, vi } from 'vitest';
import { DetailsCache } from './details.svelte';

describe('DetailsCache', () => {
  it('fetches once per blob and shares the result', async () => {
    const fetcher = vi.fn(async () => ({ available: true, speciesName: 'Sceptile' }));
    const cache = new DetailsCache(fetcher as never);
    const a = cache.get('m1', 'h1'), b = cache.get('m2', 'h1');
    await vi.waitFor(() => expect(a.state).toBe('ready'));
    expect(b.state).toBe('ready');
    expect(a.data?.speciesName).toBe('Sceptile');
    expect(fetcher).toHaveBeenCalledTimes(1);
  });
  it('marks failures without caching them forever', async () => {
    let fail = true;
    const cache = new DetailsCache(async () => { if (fail) throw new Error('x'); return { available: true } as never; });
    const first = cache.get('m1', 'h2');
    await vi.waitFor(() => expect(first.state).toBe('failed'));
    fail = false;
    const second = cache.get('m1', 'h2');
    await vi.waitFor(() => expect(second.state).toBe('ready'));
  });
  it('asks the server for the Pokémon by id', async () => {
    const fetcher = vi.fn(async (_id: string) => ({ available: false }) as never);
    new DetailsCache(fetcher).get('abc', 'h3');
    expect(fetcher).toHaveBeenCalledWith('abc');
  });
});
