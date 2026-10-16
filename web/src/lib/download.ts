import { zipSync } from 'fflate';
import { api, apiBytes } from './api';
import { bank, names, toast } from './app.svelte';
import { boxLabel, type MonEntry } from './bank.svelte';
import type { MonInfo } from './protocol';

/** The server hands out at most 500 per call; 200 keeps each answer small. */
const BATCH = 200;
const KNOWN_EXT = /^(pk\d+|pb7|pb8|pa8|pa9|ck3|xk3|bk4|rk4|sk2)$/;

/** Characters Windows won't take in a file name, and the trailing dots and spaces it drops. */
const safe = (s: string) => s.replace(/[\\/:*?"<>|]/g, '_').replace(/[. ]+$/, '');

/** The app's export name: `254 - LEAFY - 957043EC.pk3`, without the PID when it isn't known. */
export function exportName(info: MonInfo, pid: string | null, ext: string): string {
  const nickname = info.nickname.trim() || names.species(info.species);
  const parts = [String(info.species).padStart(3, '0'), nickname.toUpperCase()];
  if (pid) parts.push(pid);
  return `${safe(parts.join(' - '))}.${ext}`;
}

/** The file's own format when the app recorded one we know, else the generation's main format. */
export function extOf(info: MonInfo): string {
  const f = info.format?.toLowerCase();
  return f && KNOWN_EXT.test(f) ? f : `pk${info.generation}`;
}

export interface ZipEntry { folder: string; name: string; data: Uint8Array | null }

/** One folder per box; files that couldn't be fetched are listed in `missing.txt` instead. */
export function buildZip(entries: ZipEntry[]): Uint8Array {
  const files: Record<string, Uint8Array> = {};
  const missing: string[] = [];
  for (const e of entries) {
    const folder = safe(e.folder);
    if (!e.data) { missing.push(`${folder}/${e.name}`); continue; }
    const dot = e.name.lastIndexOf('.');
    const stem = dot > 0 ? e.name.slice(0, dot) : e.name, ext = dot > 0 ? e.name.slice(dot) : '';
    let path = `${folder}/${e.name}`;
    for (let n = 2; path in files; n++) path = `${folder}/${stem} (${n})${ext}`;
    files[path] = e.data;
  }
  if (missing.length) {
    files['missing.txt'] = new TextEncoder().encode(`These Pokémon couldn't be downloaded:\r\n${missing.join('\r\n')}\r\n`);
  }
  return zipSync(files);
}

/** Hands the browser a file to save, through a short-lived Blob URL. */
function save(data: Uint8Array, name: string, type: string): void {
  const url = URL.createObjectURL(new Blob([data as Uint8Array<ArrayBuffer>], { type }));
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  a.hidden = true;
  document.body.append(a);
  a.click();
  a.remove();
  // Revoking at once can cancel the save in some browsers.
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

function fromBase64(s: string): Uint8Array {
  const bin = atob(s);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}

/** The original .pk file, byte for byte. */
export async function downloadOne(mon: MonEntry, pid: string | null): Promise<void> {
  const info = mon.value.info;
  const data = await apiBytes(`/v1/blobs/${encodeURIComponent(mon.value.blob)}`);
  save(data, exportName(info, pid, extOf(info)), 'application/octet-stream');
}

/** Zips the files in batches; returns how many couldn't be fetched (listed in the zip's `missing.txt` and a toast). */
export async function downloadMany(mons: MonEntry[], zipName: string, onProgress?: (done: number, total: number) => void): Promise<number> {
  const sorted = [...mons].sort((a, b) => a.value.box - b.value.box || a.value.slot - b.value.slot);
  const hashes = [...new Set(sorted.map((m) => m.value.blob))];
  const got = new Map<string, Uint8Array>();
  onProgress?.(0, hashes.length);
  for (let i = 0; i < hashes.length; i += BATCH) {
    const batch = hashes.slice(i, i + BATCH);
    const res = await api<{ blobs: { hash: string; data: string }[] }>('/v1/blobs/download', { json: { hashes: batch } });
    for (const b of res?.blobs ?? []) got.set(b.hash, fromBase64(b.data));
    onProgress?.(Math.min(i + BATCH, hashes.length), hashes.length);
  }
  const entries = sorted.map((m) => ({
    folder: `${String(m.value.box + 1).padStart(2, '0')} ${boxLabel(bank.boxes, m.value.box)}`,
    name: exportName(m.value.info, null, extOf(m.value.info)),
    data: got.get(m.value.blob) ?? null,
  }));
  save(buildZip(entries), safe(zipName), 'application/zip');
  const missing = entries.filter((e) => !e.data).length;
  if (missing) toast(`${missing} Pokémon couldn't be downloaded. They're listed in missing.txt.`);
  return missing;
}
