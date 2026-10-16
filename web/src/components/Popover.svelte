<script lang="ts">
  import type { Snippet } from 'svelte';

  /**
   * An anchored panel. The trigger snippet gets `toggle` and `open`;
   * the panel closes on an outside click or Esc (focus goes back to the trigger) and keeps itself inside the viewport.
   */
  let { open = $bindable(false), align = 'right', label, cls = '', trigger, children }: {
    open?: boolean; align?: 'left' | 'right'; label: string; cls?: string;
    trigger: Snippet<[() => void, boolean]>; children: Snippet<[() => void]>;
  } = $props();

  let wrap: HTMLSpanElement;
  let panel = $state<HTMLDivElement>();
  let shift = $state(0);

  const toggle = () => (open = !open);
  function close(refocus = false) {
    open = false;
    if (refocus) (wrap.querySelector('button') as HTMLButtonElement | null)?.focus();
  }

  $effect(() => {
    if (!open) return;
    const outside = (e: PointerEvent) => { if (!wrap.contains(e.target as Node)) close(); };
    const esc = (e: KeyboardEvent) => { if (e.key === 'Escape') { e.stopPropagation(); close(true); } };
    document.addEventListener('pointerdown', outside, true);
    document.addEventListener('keydown', esc, true);
    return () => { document.removeEventListener('pointerdown', outside, true); document.removeEventListener('keydown', esc, true); };
  });

  // Nudge sideways so the panel never leaves the viewport, then focus what's chosen (or the first choice).
  $effect(() => {
    if (!open || !panel) { shift = 0; return; }
    const p = panel;
    shift = 0;
    requestAnimationFrame(() => {
      const r = p.getBoundingClientRect(), pad = 8;
      if (r.left < pad) shift = pad - r.left;
      else if (r.right > innerWidth - pad) shift = innerWidth - pad - r.right;
      const first = (p.querySelector('.on') ?? p.querySelector('button:not(:disabled)')) as HTMLElement | null;
      first?.focus({ preventScroll: true });
      // The current wallpaper may sit far down the list: scroll only that list, never the page.
      const list = first?.closest('.wplist') as HTMLElement | null;
      if (first && list) list.scrollTop = first.offsetTop - list.offsetTop - (list.clientHeight - first.offsetHeight) / 2;
    });
  });
</script>

<span class="wpwrap {cls}" bind:this={wrap}>
  {@render trigger(toggle, open)}
  {#if open}
    <div class="pop" class:left={align === 'left'} role="dialog" aria-label={label} bind:this={panel} style:translate={shift ? `${shift}px 0` : undefined}>
      {@render children(() => close(true))}
    </div>
  {/if}
</span>
