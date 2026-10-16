<script lang="ts">
  import { untrack } from 'svelte';
  import { ago } from '../lib/ago';
  import { bank, names } from '../lib/app.svelte';
  import { restoreMon } from '../lib/changes';
  import { act, locked, lockedTitle, nowUtc } from '../lib/organize.svelte';
  import type { ReleasedMon } from '../lib/protocol';
  import { released } from '../lib/released.svelte';
  import InfoTip from './InfoTip.svelte';
  import Sprite from './Sprite.svelte';

  /** Recently released: Pokémon released on any device that the server can still put back. */

  // Fetched when the view opens, and again whenever the Bank changes while it's open (the live stream's bank event
  // moves the cursor, and so does the poll after a release on another device).
  $effect(() => {
    if (!bank.loaded) return;
    void bank.cursor;
    untrack(() => { void released.load(); });
  });

  /** Server times may come without a zone; they are UTC. */
  const utc = (s: string) => new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(s) ? s : s + 'Z');
  // A Pokémon being restored is already back in the Bank (shown at once); it leaves the list then, and returns if the
  // server says no.
  const rows = $derived(released.items.filter((r) => !bank.mon(r.id)));
  const days = $derived(released.retentionDays);
  const span = $derived(days === 1 ? '1 day' : `${days} days`);
  const ro = $derived(locked());

  async function restore(r: ReleasedMon) {
    await act(() => restoreMon(bank, r, nowUtc()));
    void released.load();
  }
</script>

<main class="acct relpage" aria-label="Recently released">
  <h2 class="hrow">Recently released{#if days}<InfoTip text="Restore puts a Pokémon back in its old slot, or in the first free slot from its old box on. After {span} a release is final." label="About Recently released" />{/if}</h2>
  <p class="acct-server">
    {#if days === 0}Releases on this server are final.
    {:else if days}Pokémon released in the last {span}, on any of your devices.
    {:else}&nbsp;{/if}
  </p>

  <div class="rellist" aria-busy={!released.loaded && !released.error}>
    {#if !released.loaded}
      {#if released.error}
        <div class="relnote" role="alert"><span>{released.error}</span><button class="btn sm" onclick={() => released.load()}>Try again</button></div>
      {/if}
    {:else if rows.length === 0}
      <div class="relnote">{days ? `Nothing released in the last ${span}.` : 'Nothing to restore.'}</div>
    {:else}
      {#each rows as r (r.id)}
        {@const info = r.value.info}
        <div class="rel">
          <span class="pk"><Sprite {info} /></span>
          <div class="pn">
            <b>{info.nickname}</b>
            <small><span>{names.species(info.species)} · Lv. {info.level}{info.shiny ? ' · ★' : ''}</span><span class="when"><span class="sep">{' · '}</span>Released {ago(utc(r.releasedUtc))}</span></small>
          </div>
          <button class="btn sm" disabled={ro} title={lockedTitle() ?? `Restore ${info.nickname}`} onclick={() => restore(r)}>Restore</button>
        </div>
      {/each}
    {/if}
  </div>
</main>
