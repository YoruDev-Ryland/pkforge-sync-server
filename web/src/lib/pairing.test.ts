import { afterEach, describe, it, expect, vi } from 'vitest';
import { copyText, pairingLine, pairingServer } from './pairing';

describe('pairing line', () => {
  it('leaves out https, like the app does when it is typed', () => {
    expect(pairingLine({ protocol: 'https:', host: 'sync.example.com' }, 'K7QM4XTP')).toBe('sync.example.com  K7QM-4XTP');
    expect(pairingLine({ protocol: 'https:', host: 'sync.example.com:8443' }, 'k7qm4xtp')).toBe('sync.example.com:8443  K7QM-4XTP');
  });
  it('keeps any other scheme so it types back to the same server', () => {
    expect(pairingServer({ protocol: 'http:', host: '192.168.1.10:8080' })).toBe('http://192.168.1.10:8080');
    expect(pairingLine({ protocol: 'http:', host: 'nas:5099' }, 'K7QM-4XTP')).toBe('http://nas:5099  K7QM-4XTP');
  });
});

describe('copyText', () => {
  afterEach(() => vi.restoreAllMocks());

  it('uses the clipboard when it can', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal('navigator', { clipboard: { writeText } });
    expect(await copyText('x  ABCD-2345')).toBe(true);
    expect(writeText).toHaveBeenCalledWith('x  ABCD-2345');
    vi.unstubAllGlobals();
  });
  it('falls back to a selection copy when the clipboard is refused', async () => {
    vi.stubGlobal('navigator', { clipboard: { writeText: vi.fn().mockRejectedValue(new Error('denied')) } });
    const exec = vi.fn().mockReturnValue(true);
    (document as unknown as { execCommand: unknown }).execCommand = exec;
    expect(await copyText('y')).toBe(true);
    expect(exec).toHaveBeenCalledWith('copy');
    expect(document.querySelector('textarea')).toBeNull();
    vi.unstubAllGlobals();
  });
});
