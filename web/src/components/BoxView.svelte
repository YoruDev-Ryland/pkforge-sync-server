<script lang="ts">
  import { bank, ui } from '../lib/app.svelte';
  import type { MonEntry } from '../lib/bank.svelte';
  import { confirmRelease, releasing } from '../lib/organize.svelte';
  import { released } from '../lib/released.svelte';
  import BoxHeader from './BoxHeader.svelte';
  import ReleaseDialog from './ReleaseDialog.svelte';
  import SelectionBar from './SelectionBar.svelte';
  import SlotGrid from './SlotGrid.svelte';

  /** The box workspace: header row (the selection bar lies over it), then the box. */
  let { download }: { download?: (ids: string[]) => void } = $props();
  const mons = $derived(bank.monsIn(ui.box));
  const several = $derived([...ui.selected].filter((s) => mons[s]).length > 1);
  const toRelease = $derived(releasing.ids.map((id) => bank.mon(id)).filter((m): m is MonEntry => !!m));
</script>

<main class="work" aria-label="Box">
  <div class="headwrap">
    <BoxHeader covered={several} />
    {#if several}<SelectionBar {download} />{/if}
  </div>
  <div class="fit"><SlotGrid /></div>
</main>
{#if toRelease.length}
  <ReleaseDialog mons={toRelease} days={released.retentionDays} keep={() => (releasing.ids = [])} confirm={confirmRelease} />
{/if}
