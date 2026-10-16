import { createHash, randomBytes, randomUUID } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import type { Page } from '@playwright/test';

// Seeds accounts through the real API, the way the app would, and acts as "another device" on them.

export const PORT = 5199;
export const BASE = `http://127.0.0.1:${PORT}`;
export const PASSWORD = 'e2e-password-1';
export const PHONE = 'Test phone';
/** The server's first account, made through the web sign-up by fresh.spec.ts before anything else runs: its admin. */
export const ADMIN_EMAIL = 'admin@e2e.test';

const fixtures = fileURLToPath(new URL('./fixtures/', import.meta.url));
export const fixture = (name: string) => readFileSync(fixtures + name);

const SPECIES = ['Bulbasaur', 'Ivysaur', 'Venusaur', 'Charmander', 'Charmeleon', 'Charizard', 'Squirtle', 'Wartortle', 'Blastoise', 'Caterpie',
  'Metapod', 'Butterfree', 'Weedle', 'Kakuna', 'Beedrill', 'Pidgey', 'Pidgeotto', 'Pidgeot', 'Rattata', 'Raticate', 'Spearow', 'Fearow',
  'Ekans', 'Arbok', 'Pikachu', 'Raichu', 'Sandshrew', 'Sandslash', 'Nidoran♀', 'Nidorina', 'Nidoqueen', 'Nidoran♂', 'Nidorino', 'Nidoking',
  'Clefairy', 'Clefable', 'Vulpix', 'Ninetales', 'Jigglypuff', 'Wigglytuff'];

export const SHINY_HUNTS = 'Shiny hunts in progress';
export const BOX_COUNT = 10;

type Init = { key?: string; method?: string; json?: unknown };

export async function call<T = unknown>(path: string, init: Init = {}): Promise<{ status: number; body: T }> {
  const headers: Record<string, string> = { 'X-PKForge-Protocol': '1' };
  if (init.key) headers.authorization = `Bearer ${init.key}`;
  if (init.json !== undefined) headers['content-type'] = 'application/json';
  const r = await fetch(BASE + path, {
    method: init.method ?? (init.json !== undefined ? 'POST' : 'GET'), headers,
    body: init.json !== undefined ? JSON.stringify(init.json) : undefined,
  });
  const text = await r.text();
  return { status: r.status, body: (text ? JSON.parse(text) : undefined) as T };
}

async function ok<T>(path: string, init: Init = {}): Promise<T> {
  const r = await call<T>(path, init);
  if (r.status >= 300) throw new Error(`${init.method ?? (init.json ? 'POST' : 'GET')} ${path} → ${r.status} ${JSON.stringify(r.body)}`);
  return r.body;
}

export interface Item { kind: string; id: string; version: number; deleted: boolean; value: any }
export interface Write { kind: string; id: string; baseVersion: number; value: unknown | null; deleted?: boolean }

/** Every live item of the account, by `kind:id`, as the server holds it now. */
export async function items(key: string): Promise<Map<string, Item>> {
  const all = new Map<string, Item>();
  for (let since = 0; ;) {
    const page = await ok<{ items: Item[]; next: number; hasMore: boolean }>(`/v1/sync/changes?since=${since}&limit=1000`, { key });
    for (const i of page.items) { if (i.deleted) all.delete(`${i.kind}:${i.id}`); else all.set(`${i.kind}:${i.id}`, i); }
    since = page.next;
    if (!page.hasMore) return all;
  }
}

/** The account's Pokémon, by nickname (seeded nicknames are unique). */
export async function monsByName(key: string): Promise<Map<string, Item>> {
  const map = new Map<string, Item>();
  for (const i of (await items(key)).values()) if (i.kind === 'mon') map.set(i.value.info.nickname, i);
  return map;
}

/** Pushes change groups as the seeding device ("another device" to the browser); returns each group's status. */
export async function pushAs(key: string, groups: Write[][]): Promise<string[]> {
  const res = await ok<{ results: { status: string; message?: string }[] }>('/v1/sync/push', {
    key, json: { groups: groups.map((writes) => ({ clientGroupId: randomUUID(), writes })) },
  });
  return res.results.map((r) => r.status);
}

/** A write that moves an item to a new value on top of the version the server holds. */
export const rewrite = (item: Item, value: unknown): Write => ({ kind: item.kind, id: item.id, baseVersion: item.version, value });

const sha = (b: Uint8Array) => createHash('sha256').update(b).digest('hex');

interface Seeded { file?: string; data: Uint8Array; box: number; slot: number; info: Record<string, unknown> }

/** Wallpapers by 0-based box, over the seeded ones. */
export interface SeedOptions { wallpapers?: Record<number, string> }

/**
 * A new account with 10 boxes: Sceptile, Garchomp and Pikachu (real files) in box 1, 30 synthetic Pokémon filling box 2
 * (the living dex start box) and 10 more in box 3. `ids` maps nicknames to Pokémon ids.
 */
export async function seedAccount(email: string, opts: SeedOptions = {}): Promise<{ deviceKey: string; deviceId: string; ids: Record<string, string> }> {
  const reg = await ok<{ deviceKey: string; deviceId: string }>('/v1/accounts', { json: { email, password: PASSWORD, deviceName: PHONE } });
  const key = reg.deviceKey;

  const mons: Seeded[] = [
    { file: 'sceptile.pk3', box: 0, slot: 0, info: { species: 254, nickname: 'LEAFY', level: 62, generation: 3, sourceName: 'Emerald', format: 'pk3', heldItem: 200 } },
    { file: 'garchomp.pk4', box: 0, slot: 1, info: { species: 445, nickname: 'Garchomp', level: 55, generation: 4, sourceName: 'Platinum', format: 'pk4' } },
    { file: 'pikachu.pk9', box: 0, slot: 2, info: { species: 25, nickname: 'Pikachu', level: 12, generation: 9, sourceName: 'Scarlet', format: 'pk9' } },
  ].map((m) => ({ ...m, data: fixture(m.file!) }));
  SPECIES.forEach((name, i) => mons.push({
    data: randomBytes(344), box: 1 + Math.floor(i / 30), slot: i % 30,
    info: { species: i + 1, nickname: name, level: 5 + i, generation: 9, sourceName: 'Scarlet', format: 'pk9' },
  }));

  const blobs = mons.map((m) => ({ hash: sha(m.data), data: Buffer.from(m.data).toString('base64') }));
  await ok('/v1/blobs/upload', { key, json: { blobs } });

  const ids: Record<string, string> = {};
  const when = '2026-10-01T12:00:00+00:00';
  const writes: Write[] = mons.map((m, i) => {
    const id = randomUUID();
    ids[m.info.nickname as string] = id;
    return {
      kind: 'mon', id, baseVersion: 0,
      value: { box: m.box, slot: m.slot, movedUtc: when, addedUtc: when, blob: blobs[i].hash, info: { form: 0, shiny: false, ...m.info } },
    };
  });
  const names = Array.from({ length: BOX_COUNT }, (_, i) => (i === 0 ? 'Keepers' : i === BOX_COUNT - 1 ? SHINY_HUNTS : null));
  const wallpapers = Array.from({ length: BOX_COUNT }, (_, i) => opts.wallpapers?.[i] ?? (i === 0 ? 'color:2' : i === BOX_COUNT - 1 ? 'art:box_wp01e' : null));
  writes.push({ kind: 'boxes', id: 'bank', baseVersion: 0, value: { count: BOX_COUNT, names, wallpapers } });
  writes.push({ kind: 'settings', id: 'bank', baseVersion: 0, value: { livingDexStartBox: 1 } });
  const status = await pushAs(key, [writes]);
  if (status[0] !== 'accepted') throw new Error(`Seeding ${email} was ${status[0]}`);
  return { deviceKey: key, deviceId: reg.deviceId, ids };
}

let counter = 0;
/** A fresh address per test, unique across workers and runs. */
export const newEmail = (tag: string) => `${tag}-${process.pid}-${Date.now()}-${counter++}@e2e.test`;

/** Signs the page's browser context in with the password (sets the device cookie) without going through the form. */
export async function signInApi(page: Page, email: string, password = PASSWORD): Promise<void> {
  const r = await page.request.post('/v1/web/session', {
    headers: { 'X-PKForge-Protocol': '1' },
    data: { email, password, deviceName: 'Chrome on Linux' },
  });
  if (!r.ok()) throw new Error(`web sign-in → ${r.status()}`);
}

/** Seeds an account, signs this page in and opens the Bank on `path`, waiting until the boxes are there. */
export async function openBank(page: Page, tag: string, path = '/bank/1', opts: SeedOptions = {}) {
  const email = newEmail(tag);
  const seeded = await seedAccount(email, opts);
  await signInApi(page, email);
  await page.goto(path);
  // The sidebar list and status line are hidden on narrow screens, so wait for them in the DOM, not on screen.
  await page.locator('.boxrow[data-box="9"]').waitFor({ state: 'attached' });
  await page.locator('.side-foot .status', { hasText: /^(Synced|Live)/ }).waitFor({ state: 'attached' });
  return { email, ...seeded };
}

/** Wakes the poller (and a disconnected live stream) the way switching back to the tab does. */
export const focusTab = (page: Page) => page.evaluate(() => document.dispatchEvent(new Event('visibilitychange')));

/** A slot of the open box, 0-based. */
export const slot = (page: Page, n: number) => page.locator(`.grid .slot[data-slot="${n}"]`);
/** A sidebar box row, 0-based. */
export const boxRow = (page: Page, n: number) => page.locator(`.boxrow[data-box="${n}"]`);
