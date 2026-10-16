<script lang="ts">
  import { bank } from '../lib/app.svelte';
  import { wallpaperOf } from '../lib/wallpapers';

  /** A box's 6×5 occupancy minimap on its wallpaper. */
  let { box }: { box: number } = $props();
  const wp = $derived(wallpaperOf(bank.boxes, box));
  const mons = $derived(bank.monsIn(box));
</script>

<span class="mini" class:art={!!wp.url} style:background-color={wp.tone} style:background-image={wp.url ? `url("${wp.url}")` : undefined} aria-hidden="true"
  >{#each mons as m, s (s)}<i class:f={!!m}></i>{/each}</span>
