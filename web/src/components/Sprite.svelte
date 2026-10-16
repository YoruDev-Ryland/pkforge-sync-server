<script lang="ts">
  import type { MonInfo } from '../lib/protocol';
  import { spriteCandidates } from '../lib/sprites';

  /** A Pokémon's pixel sprite: walks the app's candidates on load errors, then a neutral tile with the dex number. */
  let { info, alt = '' }: { info: MonInfo; alt?: string } = $props();

  // The app's last candidate, b_0, is a fully transparent placeholder: the dex-number tile stands in for it.
  const list = $derived(spriteCandidates(info).filter((u) => !u.endsWith('/b_0.png')));
  // URLs that failed to load. The handler reads only the element, never a derived: an image can fail after unmount.
  const failed = $state<string[]>([]);
  const src = $derived(list.find((u) => !failed.includes(u)));
</script>

{#if src}
  <img {src} {alt} draggable="false" onerror={(e) => failed.push((e.currentTarget as HTMLImageElement).getAttribute('src') ?? '')} />
{:else}
  <span class="dexno" role={alt ? 'img' : undefined} aria-label={alt || undefined}>#{String(info.species).padStart(3, '0')}</span>
{/if}
