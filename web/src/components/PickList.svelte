<script lang="ts">
  import { bank, names, ui } from '../lib/app.svelte';
  import { ICON } from '../lib/icons';
  import { wallpaperOf } from '../lib/wallpapers';
  import InfoTip from './InfoTip.svelte';
  import Sprite from './Sprite.svelte';

  /** The occupied selected slots, each removable from the selection. */
  let { slots }: { slots: number[] } = $props();

  const mons = $derived(bank.monsIn(ui.box));
  const wp = $derived(wallpaperOf(bank.boxes, ui.box));

  function unpick(slot: number) {
    const next = new Set(ui.selected);
    next.delete(slot);
    ui.selected = next;
    if (ui.anchor === slot) ui.anchor = [...next][0];
  }
</script>

<div class="emptyinsp">
  <h3 class="hrow">{slots.length} Pokémon selected<InfoTip text="Move, download or release them all with the bar above the box. <kbd>Shift</kbd>-click selects a range; <kbd>Ctrl</kbd>-click adds or removes one." /></h3>
</div>
<ul class="picklist">
  {#each slots as s (mons[s]?.id ?? s)}
    {@const info = mons[s]!.value.info}
    <li>
      <span class="pk" class:pic={!!wp.url} style:background-color={wp.tone} style:background-image={wp.url ? `url("${wp.url}")` : undefined}><Sprite {info} /></span>
      <span class="pn"><b>{info.nickname}</b><small>{names.species(info.species)} · Lv. {info.level}{info.shiny ? ' · ★' : ''}</small></span>
      <span class="ps">Slot {s + 1}</span>
      <button class="x2" aria-label="Remove {info.nickname} from selection" title="Remove from selection" onclick={() => unpick(s)}>{@html ICON.x}</button>
    </li>
  {/each}
</ul>
