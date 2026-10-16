<script lang="ts">
  import { untrack } from 'svelte';
  import { friendlyError } from '../lib/api';
  import { bank, session, toast, ui } from '../lib/app.svelte';
  import { boxLabel, type MonEntry } from '../lib/bank.svelte';
  import { downloadMany, downloadOne } from '../lib/download';
  import { router } from '../lib/router.svelte';
  import Account from './Account.svelte';
  import BoxView from './BoxView.svelte';
  import Inspector from './Inspector.svelte';
  import Released from './Released.svelte';
  import Sidebar from './Sidebar.svelte';

  const route = $derived(router.route);

  // /bank/{n} picks the box; past the last box it lands on the last one.
  $effect(() => {
    if (route.page !== 'bank' || route.box === null || !bank.loaded) return;
    const box = Math.min(route.box, bank.boxCount - 1);
    // Another box starts with its first slot selected; the inspector keeps its tab.
    untrack(() => { if (ui.box !== box) Object.assign(ui, { box, selected: new Set([0]), anchor: 0 }); });
    if (box !== route.box) router.go(`/bank/${box + 1}`, true);
  });

  function saveOne(mon: MonEntry, pid: string | null) {
    downloadOne(mon, pid).catch((e) => toast(friendlyError(e)));
  }
  function saveMany(ids: string[]) {
    const mons = ids.map((id) => bank.mon(id)).filter((m): m is MonEntry => !!m);
    downloadMany(mons, `${boxLabel(bank.boxes, ui.box)}.zip`).catch((e) => toast(friendlyError(e)));
  }

  // The device count in the status line is refreshed on Account visits.
  $effect(() => { if (route.page === 'account') untrack(() => session.refreshDevices()); });
</script>

<div class="app" class:two={route.page === 'account' || route.page === 'released'}>
  <Sidebar on={route.page === 'account' ? 'acct' : route.page === 'released' ? 'released' : 'bank'} />
  {#if route.page === 'account'}
    <Account />
  {:else if route.page === 'released'}
    {#if bank.loaded}<Released />{:else}<main class="acct" aria-label="Recently released" aria-busy="true"></main>{/if}
  {:else}
    {#if bank.loaded}<BoxView download={saveMany} />{:else}<main class="work" aria-label="Box" aria-busy="true"></main>{/if}
    {#if bank.loaded}<Inspector onDownload={saveOne} />{:else}<aside class="insp" aria-label="Pokémon" tabindex="-1"></aside>{/if}
  {/if}
</div>
