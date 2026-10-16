<script lang="ts">
  import { CATEGORY } from '../lib/icons';
  import type { MonDetails } from '../lib/protocol';
  import { typeColor, typeDark } from '../lib/statcolor';
  import InfoTip from './InfoTip.svelte';

  /** Moves tab: one aligned list on shared columns, always four rows. */
  let { d }: { d: MonDetails } = $props();

  const blanks = $derived(Math.max(0, 4 - d.moves.length));
  const tip = $derived(`Current / max PP. Gold dots are PP Ups used (up to 3).${d.generation <= 3 ? ' In Gen 1–3, a move is physical or special by its type, as it was in that game.' : ''}`);
</script>

<div class="sec">
  <div class="mvlist">
    <span class="h l">Type</span><span class="h l">Move</span><span class="h">Pow</span><span class="h">Acc</span>
    <span class="h"><span class="hrow" style="justify-content:flex-end">PP<InfoTip text={tip} /></span></span>
    {#each d.moves as m, k (k)}
      {@const [icon, label] = CATEGORY[m.category] ?? CATEGORY.status}
      <div class="mvrow">
        <span class="tcell"><span class="tbadge" class:dk={typeDark(m.type)} style:background={typeColor(m.type)}>{m.type}</span></span>
        <span class="mvname"><b>{m.name}</b><small>{@html icon}{label}</small></span>
        <span class="num" class:dim={!m.power}>{m.power || '—'}</span>
        <span class="num" class:dim={!m.accuracy}>{m.accuracy || '—'}</span>
        <span class="ppcell"><span class="mono">{m.pp}/{m.maxPp}</span><span class="ups" title="{m.ppUps} PP Up{m.ppUps === 1 ? '' : 's'} used">{#each [0, 1, 2] as u (u)}<i class:on={u < m.ppUps}></i>{/each}</span></span>
      </div>
    {/each}
    {#each { length: blanks } as _, k (k)}
      <div class="mvrow empty"><span class="tcell"></span><span class="mvname"><b>—</b></span><span></span><span></span><span></span></div>
    {/each}
  </div>
</div>
