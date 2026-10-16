/** This machine, by name or loopback address: plain http to it never crosses a network. */
const LOCAL = /^(localhost|.+\.localhost|127(?:\.\d{1,3}){3}|\[::1\]|::1)$/i;

/** The page came over plain http from another machine, so passwords cross the network unencrypted. */
export function unencryptedRemote(loc: { protocol: string; hostname: string }): boolean {
  return loc.protocol === 'http:' && !LOCAL.test(loc.hostname);
}
