/**
 * The line a camera-less device types under Bank Sync › Connect with a code: "<server>  XXXX-XXXX".
 * The scheme is left out for https, the app's default for a public address; anything else keeps it.
 */
export function pairingServer(loc: { protocol: string; host: string }): string {
  return loc.protocol === 'https:' ? loc.host : `${loc.protocol}//${loc.host}`;
}

export function pairingLine(loc: { protocol: string; host: string }, code: string): string {
  const c = code.replace(/[\s-]/g, '').toUpperCase();
  return `${pairingServer(loc)}  ${c.slice(0, 4)}-${c.slice(4)}`;
}

/** Copies text; falls back to a hidden textarea where the async clipboard is missing or refused (plain http). */
export async function copyText(text: string): Promise<boolean> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch { /* fall through */ }
  const area = document.createElement('textarea');
  area.value = text;
  area.setAttribute('readonly', '');
  area.style.position = 'fixed';
  area.style.opacity = '0';
  document.body.appendChild(area);
  area.select();
  try { return document.execCommand('copy'); }
  catch { return false; }
  finally { area.remove(); }
}
