<script lang="ts">
  import { onDestroy } from 'svelte';
  import { ago, countdown } from '../lib/ago';
  import { api, ApiError, friendlyError } from '../lib/api';
  import { app, bank, prefs, session, toast, ui } from '../lib/app.svelte';
  import { downloadMany } from '../lib/download';
  import { ICON } from '../lib/icons';
  import type { DeviceInfo, PairingCodeResponse } from '../lib/protocol';
  import { router } from '../lib/router.svelte';
  import { BOX_THEME, hueOf, THEMES, type Mode } from '../lib/theme';
  import { wallpaperOf } from '../lib/wallpapers';
  import { copyText, pairingLine } from '../lib/pairing';
  import InfoTip from './InfoTip.svelte';

  /** The Account page: section name and (i) on the left, controls on the right. */

  const MODES: [Mode, 'auto' | 'sun' | 'moon', string][] = [['system', 'auto', 'System'], ['light', 'sun', 'Light'], ['dark', 'moon', 'Dark']];
  // Match box's swatch: the last viewed box's wallpaper with the theme it picks.
  const wp = $derived(wallpaperOf(bank.boxes, ui.box));

  // ── Devices ──
  /** Server times may come without a zone; they are UTC. */
  const utc = (s: string) => new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(s) ? s : s + 'Z');
  const isWeb = (d: DeviceInfo) => d.isCurrent || /^(Firefox|Chrome|Edge|Safari|Web browser)\b/.test(d.name);
  function meta(d: DeviceInfo): string {
    if (d.isCurrent) return 'Web · now';
    const synced = d.lastSyncUtc ? `synced ${ago(utc(d.lastSyncUtc))}` : 'never synced';
    return `${isWeb(d) ? 'Web' : 'PKForge app'} · ${synced}`;
  }

  let asking = $state<string | null>(null);
  let askTimer: ReturnType<typeof setTimeout> | undefined;
  function ask(id: string | null) {
    clearTimeout(askTimer);
    asking = id;
    if (id) askTimer = setTimeout(() => (asking = null), 5000);
  }
  async function remove(d: DeviceInfo) {
    ask(null);
    try {
      await api(`/v1/devices/${encodeURIComponent(d.id)}`, { method: 'DELETE' });
      toast(`Removed ${d.name}`);
    } catch (e) { toast(friendlyError(e)); }
    await session.refreshDevices();
  }

  // ── Pairing code ── made on demand: each one is a server-side secret with a lifetime.
  let pair = $state<{ code: string; expires: number } | null>(null);
  let pairErr = $state<string | null>(null);
  let pairBusy = $state(false);
  let now = $state(Date.now());
  const left = $derived(pair ? pair.expires - now : 0);
  const expired = $derived(!!pair && left <= 0);

  async function showCode() {
    pairBusy = true; pairErr = null;
    try {
      const r = await api<PairingCodeResponse>('/v1/pairing', { method: 'POST' });
      now = Date.now();
      pair = { code: r.code, expires: utc(r.expiresUtc).getTime() };
    } catch (e) { pairErr = friendlyError(e); }
    finally { pairBusy = false; }
  }
  const line = $derived(pair ? pairingLine(location, pair.code) : '');
  async function copyLine() {
    toast(await copyText(line) ? 'Copied.' : 'Couldn\'t copy. Select the line and copy it instead.');
  }
  $effect(() => {
    const p = pair;
    if (!p) return;
    const t = setInterval(() => { now = Date.now(); if (now >= p.expires) clearInterval(t); }, 1000);
    return () => clearInterval(t);
  });

  // ── Password ──
  let current = $state(''), next = $state('');
  let pwErr = $state('');
  let pwBusy = $state(false);
  async function changePassword(e: SubmitEvent) {
    e.preventDefault();
    pwErr = '';
    if (!current) { pwErr = 'Enter your current password.'; return; }
    if (next.length < 8) { pwErr = 'Use at least 8 characters.'; return; }
    pwBusy = true;
    try {
      await api('/v1/auth/password', { json: { currentPassword: current, newPassword: next, signOutOtherDevices: false } });
      current = ''; next = '';
      toast('Password updated');
    } catch (err) {
      pwErr = err instanceof ApiError && err.code === 'invalid_credentials' ? 'The current password is wrong.' : friendlyError(err);
    } finally { pwBusy = false; }
  }

  // ── Your Bank ──
  const monCount = $derived(bank.allMons().length);
  const counts = $derived(`${bank.boxCount} ${bank.boxCount === 1 ? 'box' : 'boxes'} · ${monCount} Pokémon`);
  let progress = $state<{ done: number; total: number } | null>(null);
  async function downloadAll() {
    progress = { done: 0, total: 0 };
    try {
      await downloadMany(bank.allMons(), 'PKForge Bank.zip', (done, total) => (progress = { done, total }));
    } catch (e) { toast(friendlyError(e)); }
    finally { progress = null; }
  }

  async function signOut() {
    try {
      await session.signOut();
      router.go('/signin');
    } catch (e) { toast(friendlyError(e)); }
  }

  onDestroy(() => clearTimeout(askTimer));
</script>

<main class="acct" aria-label="Account">
  <h2>Account</h2>
  <p class="acct-server">Connected to {session.info?.name ?? 'this server'} · {location.host}</p>

  <section class="setrow">
    <div><h3 class="hrow">Devices<InfoTip text="Everything that syncs this Bank. Removing a device signs it out; its own copy of the Bank stays on it." /></h3></div>
    <div style="display:grid;gap:14px;min-width:0">
      <div class="devs">
        {#each session.devices as d (d.id)}
          <div class="dev">
            <span class="ic">{@html isWeb(d) ? ICON.web : ICON.phone}</span>
            <div style="min-width:0"><div class="n">{d.name}</div><div class="m">{meta(d)}</div></div>
            {#if d.isCurrent}
              <span class="here">This browser</span>
            {:else if asking === d.id}
              <span class="ask"><span>Remove?</span><button class="btn sm danger" onclick={() => remove(d)}>Yes</button><button class="btn sm" onclick={() => ask(null)}>No</button></span>
            {:else}
              <button class="btn sm rm" onclick={() => ask(d.id)}>Remove</button>
            {/if}
          </div>
        {/each}
      </div>
      <div class="pair">
        {#if pair}
          <span class="paircode" class:gone={expired} aria-label="Pairing code {pair.code}">{pair.code.slice(0, 4)}<span>-</span>{pair.code.slice(4)}</span>
          <span class="hrow" style="font-size:.85rem;color:var(--text-2)">
            {#if expired}
              Expired ·<button class="linkbtn" onclick={showCode} disabled={pairBusy}>Show a new code</button>
            {:else}
              Add a device · {countdown(left)} left<InfoTip text="On the new device, open Settings › Bank Sync › Connect with a code and type the line below: the server, then the code. It works once." />
            {/if}
          </span>
          {#if !expired}
            <span class="pairline">
              <code aria-label="Server and code to type">{line}</code>
              <button class="btn sm" onclick={copyLine}>Copy</button>
            </span>
          {/if}
        {:else}
          <button class="btn sm" onclick={showCode} disabled={pairBusy}>{@html ICON.plus}Show a code</button>
          <span class="hrow" style="font-size:.85rem;color:var(--text-2)">Add a device<InfoTip text="Shows the server and a code to type on the new device under Settings › Bank Sync › Connect with a code. It works once, for a few minutes." /></span>
        {/if}
      </div>
      {#if pairErr}<p class="formerr" role="alert" style="margin:0">{pairErr}</p>{/if}
    </div>
  </section>

  <section class="setrow">
    <div><h3 class="hrow">Appearance<InfoTip text="Saved to your account, so every browser you sign in on looks the same. Match box takes the color closest to the wallpaper of the box you're viewing. The app keeps its own theme under Settings › Misc." /></h3></div>
    <div style="display:grid;grid-template-columns:minmax(0,1fr);gap:14px;min-width:0">
      <div class="modes" role="radiogroup" aria-label="Mode">
        {#each MODES as [id, ic, label] (id)}
          <button role="radio" aria-checked={prefs.mode === id} class:on={prefs.mode === id} onclick={() => prefs.set({ mode: id })}>{@html ICON[ic]}{label}</button>
        {/each}
      </div>
      <div class="swatches" role="radiogroup" aria-label="Color theme">
        <button role="radio" aria-checked={prefs.theme === BOX_THEME} class="swatch box" class:on={prefs.theme === BOX_THEME} onclick={() => prefs.set({ theme: BOX_THEME })}
          ><i class="wp" style:background-color={wp.tone} style:background-image={wp.url ? `url("${wp.url}")` : undefined}><b style:background={hueOf(prefs.boxTheme)}></b></i><span>Match box</span></button>
        {#each THEMES as [id, label, hex] (id)}
          <button role="radio" aria-checked={prefs.theme === id} class="swatch" class:on={prefs.theme === id} onclick={() => prefs.set({ theme: id })}><i style:background={hex}></i><span>{label}</span></button>
        {/each}
      </div>
    </div>
  </section>

  <section class="setrow">
    <div><h3 class="hrow">Password<InfoTip text="Changing it keeps your other devices signed in." /></h3></div>
    <form class="pwform" onsubmit={changePassword} aria-busy={pwBusy} novalidate>
      <div class="field"><label for="cpw">Current password</label><input id="cpw" type="password" autocomplete="current-password" bind:value={current} /></div>
      <div class="field"><label for="npw">New password</label><input id="npw" type="password" autocomplete="new-password" bind:value={next} /><span class="hint">At least 8 characters.</span></div>
      <div><button class="btn primary" type="submit" disabled={pwBusy}>Update password</button></div>
      {#if pwErr}<p class="formerr" role="alert" style="margin:0">{pwErr}</p>{/if}
    </form>
  </section>

  <section class="setrow">
    <div><h3>Your Bank</h3></div>
    <div style="display:grid;gap:10px">
      <div style="font-size:.9rem;color:var(--text-2)">{counts}</div>
      <div class="pair" style="gap:12px">
        <button class="btn sm" onclick={downloadAll} disabled={!!progress || monCount === 0}>{@html ICON.down}Download everything (.zip)</button>
        {#if progress}<span role="status" style="font-size:.85rem;color:var(--text-2)">{progress.total ? `Fetching ${progress.done} of ${progress.total}…` : 'Starting…'}</span>{/if}
      </div>
    </div>
  </section>

  <section class="setrow">
    <div><h3 class="hrow">Sign out<InfoTip text="Removes this browser from your devices. Your Bank stays on the server and on your other devices." /></h3></div>
    <div><button class="btn sm danger" onclick={signOut}>Sign out</button></div>
  </section>
</main>
