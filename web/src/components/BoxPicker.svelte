<script lang="ts">
  import { bank } from '../lib/app.svelte';
  import { boxLabel } from '../lib/bank.svelte';
  import { freeSlots } from '../lib/changes';
  import Mini from './Mini.svelte';

  /** Every box but `from`, with its minimap and free count; boxes without room for `need` are disabled. */
  let { from, need, choose }: { from: number; need: number; choose: (box: number) => void } = $props();

  const rows = $derived(Array.from({ length: bank.boxCount }, (_, i) => i).filter((i) => i !== from)
    .map((i) => ({ i, name: boxLabel(bank.boxes, i), free: freeSlots(bank, i).length })));
</script>

<div class="pop-t">Move {need} to</div>
<div class="boxpicks">
  {#each rows as r (r.i)}
    <button class="boxpick-row" disabled={r.free < need} title={r.free < need ? `${r.name} only has ${r.free} free` : r.name} onclick={() => choose(r.i)}>
      <Mini box={r.i} /><span class="nm">{r.name}</span><span class="fr">{r.free} free</span>
    </button>
  {/each}
</div>
