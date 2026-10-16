<script lang="ts">
  import type { MonDetails } from '../lib/protocol';
  import { ribbonColor } from '../lib/statcolor';
  import Radar from './Radar.svelte';

  /** Ribbons tab: contest condition and sheen where the format has them, then the ribbons. */
  let { d }: { d: MonDetails } = $props();

  const c = $derived(d.contest);
</script>

{#if c}
  <div class="sec">
    <h4>Condition <span>from Pokéblocks{d.generation === 4 ? ' / Poffins' : ''}</span></h4>
    <Radar labels={['Cool', 'Beauty', 'Cute', 'Smart', 'Tough']} values={[c.cool, c.beauty, c.cute, c.smart, c.tough]} max={255} color="#e0567a" label="Contest condition" />
    <h4>Sheen <span class="mono">{c.sheen} / 255</span></h4>
    <div class="meter"><i style:width="{Math.round((c.sheen / 255) * 100)}%" style="background:linear-gradient(90deg,#9fd8f0,#e8c8f8,#f8e8a0)"></i></div>
  </div>
{/if}
<div class="sec">
  <h4>Ribbons <span>{d.ribbons.length}</span></h4>
  {#if d.ribbons.length}
    <div class="ribbons">{#each d.ribbons as r (r)}<span class="rib" title={r}><i style:background={ribbonColor(r)}></i><span>{r}</span></span>{/each}</div>
  {:else}
    <p class="note">No ribbons yet.</p>
  {/if}
</div>
