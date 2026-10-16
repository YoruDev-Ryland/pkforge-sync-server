import { describe, expect, it } from 'vitest';
import { unencryptedRemote } from './connection';

describe('unencryptedRemote', () => {
  it('is true for plain http to another machine', () => {
    expect(unencryptedRemote({ protocol: 'http:', hostname: '192.168.1.20' })).toBe(true);
    expect(unencryptedRemote({ protocol: 'http:', hostname: 'nas.lan' })).toBe(true);
  });

  it('is false over https', () => {
    expect(unencryptedRemote({ protocol: 'https:', hostname: 'sync.example.com' })).toBe(false);
  });

  it('is false for this machine', () => {
    for (const hostname of ['localhost', 'LOCALHOST', 'bank.localhost', '127.0.0.1', '127.1.2.3', '[::1]', '::1'])
      expect(unencryptedRemote({ protocol: 'http:', hostname }), hostname).toBe(false);
  });
});
