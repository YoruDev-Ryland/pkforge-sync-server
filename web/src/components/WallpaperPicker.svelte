<script lang="ts">
  import { ART_GROUPS, PALETTE } from '../lib/wallpapers';

  /** The app's wallpaper catalog: the 8 palette colors, then the in-game wallpapers by game. */
  let { current, choose }: { current: string; choose: (id: string) => void } = $props();
</script>

<div class="pop-t">Wallpaper</div>
<div class="wplist">
  <div class="wpdots">
    {#each PALETTE as color, i (i)}
      {@const id = `color:${i}`}
      <button class:on={id === current} aria-pressed={id === current} title="Color {i + 1}" aria-label="Color {i + 1}" onclick={() => choose(id)}><span style:background-color={color}></span></button>
    {/each}
  </div>
  {#each ART_GROUPS as group (group.title)}
    <div class="pop-t wpgroup">{group.title}</div>
    <div class="wparts">
      {#each group.assets as asset, k (asset)}
        {@const id = `art:${asset}`}
        <button class:on={id === current} aria-pressed={id === current} title="{group.title} {k + 1}" aria-label="{group.title} {k + 1}" onclick={() => choose(id)}>
          <img src="/pkhex/wallpapers/{asset}.png" alt="" loading="lazy" />
        </button>
      {/each}
    </div>
  {/each}
</div>
