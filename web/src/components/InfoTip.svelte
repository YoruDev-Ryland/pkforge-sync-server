<script module lang="ts">
  // One tip on screen at a time, across every (i) button.
  let seq = 0;
  const open = $state({ id: 0, pinned: false });
</script>

<script lang="ts">
  import { ICON } from '../lib/icons';

  /** `text` is our own copy and may contain simple markup such as <kbd>. */
  let { text, label = 'More info' }: { text: string; label?: string } = $props();

  const id = ++seq;
  const tipId = `tip${id}`;
  let button: HTMLButtonElement;
  let box = $state<HTMLDivElement>();
  let place = $state<{ left: number; top: number; side: 'top' | 'bottom'; ax: number } | null>(null);
  const shown = $derived(open.id === id);

  function show(pinned: boolean) { open.id = id; open.pinned = pinned; }
  function hide() { if (open.id === id) { open.id = 0; open.pinned = false; } }

  // Above the button when there's room, else below; always inside the viewport.
  $effect(() => {
    if (!shown || !box) { place = null; return; }
    const r = button.getBoundingClientRect(), b = box.getBoundingClientRect(), pad = 12;
    const left = Math.min(Math.max(pad, r.left + r.width / 2 - b.width / 2), innerWidth - b.width - pad);
    const above = r.top - b.height - 10 > pad;
    place = { left, top: above ? r.top - b.height - 10 : r.bottom + 10, side: above ? 'top' : 'bottom', ax: r.left + r.width / 2 - left };
  });

  function portal(node: HTMLElement) {
    document.body.appendChild(node);
    return { destroy: () => node.remove() };
  }
</script>

<svelte:document
  onclick={(e) => { if (shown && open.pinned && !button.contains(e.target as Node)) hide(); }}
  onkeydown={(e) => { if (e.key === 'Escape') hide(); }}
  onscrollcapture={() => { if (shown) hide(); }}
/>

<button
  bind:this={button}
  type="button"
  class="info"
  aria-label={label}
  aria-expanded={shown}
  aria-describedby={shown ? tipId : undefined}
  onpointerenter={(e) => { if (e.pointerType === 'mouse' && !shown) show(false); }}
  onpointerleave={(e) => { if (e.pointerType === 'mouse' && !open.pinned) hide(); }}
  onfocus={() => { if (!shown) show(false); }}
  onblur={hide}
  onclick={(e) => { e.preventDefault(); if (shown && open.pinned) hide(); else show(true); }}
>{@html ICON.info}</button>

{#if shown}
  <div
    use:portal
    bind:this={box}
    class="tipbox"
    role="tooltip"
    id={tipId}
    data-side={place?.side ?? 'top'}
    style:left="{place?.left ?? 0}px"
    style:top="{place?.top ?? 0}px"
    style:--ax="{place?.ax ?? 0}px"
    style:visibility={place ? 'visible' : 'hidden'}
  >{@html text}</div>
{/if}
