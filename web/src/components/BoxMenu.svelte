<script lang="ts">
  import { onMount, tick } from 'svelte';

  /** A box row's context menu (right-click, long-press or the menu key): "Delete box" for an empty box. */
  let { x, y, empty, last, remove, close }: { x: number; y: number; empty: boolean; last: boolean; remove: () => void; close: () => void } = $props();

  let menu: HTMLDivElement;
  let pos = $state({ left: 0, top: 0 });
  let placed = $state(false);

  onMount(() => {
    const r = menu.getBoundingClientRect(), pad = 8; // measured at 0,0 while hidden
    pos = { left: Math.max(pad, Math.min(x, innerWidth - r.width - pad)), top: Math.max(pad, Math.min(y, innerHeight - r.height - pad)) };
    placed = true;
    void tick().then(() => (menu.querySelector('button') as HTMLButtonElement | null)?.focus());
    const outside = (e: PointerEvent) => { if (!menu.contains(e.target as Node)) close(); };
    const key = (e: KeyboardEvent) => { if (e.key === 'Escape' || e.key === 'Tab') { e.preventDefault(); close(); } };
    const away = () => close();
    document.addEventListener('pointerdown', outside, true);
    document.addEventListener('keydown', key, true);
    addEventListener('resize', away);
    addEventListener('scroll', away, true);
    return () => {
      document.removeEventListener('pointerdown', outside, true);
      document.removeEventListener('keydown', key, true);
      removeEventListener('resize', away);
      removeEventListener('scroll', away, true);
    };
  });

  const why = $derived(last ? 'The Bank needs at least one box' : !empty ? 'Empty the box first' : undefined);
</script>

<div class="pop menu" role="menu" tabindex="-1" bind:this={menu} style:left="{pos.left}px" style:top="{pos.top}px" style:visibility={placed ? undefined : 'hidden'}>
  <!-- Disabled items stay focusable (aria-disabled) so the reason in the title can be reached. -->
  <button class="menuitem danger" role="menuitem" aria-disabled={why ? 'true' : undefined} title={why} onclick={() => { if (!why) { remove(); close(); } }}>Delete box</button>
  {#if why}<span class="menunote">{why}</span>{/if}
</div>
