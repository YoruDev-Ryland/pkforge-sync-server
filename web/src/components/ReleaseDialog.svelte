<script lang="ts">
  import { onMount } from 'svelte';
  import type { MonEntry } from '../lib/bank.svelte';
  import { releaseNote } from '../lib/released.svelte';
  import Sprite from './Sprite.svelte';

  /**
   * Release confirmation: Keep is focused and is what Esc does; focus stays inside until it closes.
   * `days` is the server's restore window (Recently released); 0 or unknown keeps the app-snapshot note.
   */
  let { mons, days = null, keep, confirm }: { mons: MonEntry[]; days?: number | null; keep: () => void; confirm: () => void } = $props();

  const one = $derived(mons.length === 1);
  let dialog: HTMLDivElement;
  let keepBtn: HTMLButtonElement;

  onMount(() => {
    const back = document.activeElement as HTMLElement | null;
    keepBtn.focus();
    return () => back?.focus?.({ preventScroll: true });
  });

  function keydown(e: KeyboardEvent) {
    if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); keep(); return; }
    if (e.key !== 'Tab') return;
    const items = [...dialog.querySelectorAll<HTMLElement>('button')];
    const at = items.indexOf(document.activeElement as HTMLElement);
    const next = (at + (e.shiftKey ? -1 : 1) + items.length) % items.length;
    e.preventDefault();
    items[next].focus();
  }
</script>

<svelte:window onkeydown={keydown} />
<div class="scrim" role="presentation" onclick={(e) => { if (e.target === e.currentTarget) keep(); }}>
  <div class="modal" role="dialog" aria-modal="true" aria-labelledby="relTitle" bind:this={dialog}>
    <h3 id="relTitle">Release {one ? mons[0].value.info.nickname : `${mons.length} Pokémon`}?</h3>
    <div class="who">
      {#each mons.slice(0, 8) as m (m.id)}<Sprite info={m.value.info} alt={m.value.info.nickname} />{/each}
    </div>
    <p>{one ? "It's" : "They're"} removed from the Bank on every synced device. {releaseNote(days, one)}</p>
    <div class="row">
      <button class="btn" bind:this={keepBtn} onclick={keep}>Keep</button>
      <button class="btn danger solid" onclick={confirm}>Release</button>
    </div>
  </div>
</div>
