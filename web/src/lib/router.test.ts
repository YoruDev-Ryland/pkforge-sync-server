import { describe, it, expect } from 'vitest';
import { parseRoute } from './router.svelte';

describe('parseRoute', () => {
  it.each([
    ['/signin', { page: 'signin' }],
    ['/bank', { page: 'bank', box: null }],
    ['/bank/3', { page: 'bank', box: 2 }],
    ['/bank/0', { page: 'bank', box: null }],
    ['/account', { page: 'account' }],
    ['/bank/released', { page: 'released' }],
    ['/whatever', { page: 'bank', box: null }],
  ])('%s', (path, route) => expect(parseRoute(path)).toEqual(route));
});
