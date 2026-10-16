<script lang="ts">
  import { bank, names, ui } from '../lib/app.svelte';
  import { boxLabel } from '../lib/bank.svelte';
  import { moveMon } from '../lib/changes';
  import { ICON } from '../lib/icons';
  import { dexNo, FALLBACK_MAX_SPECIES, slotLabel, slotSpecies } from '../lib/livingdex';
  import { act, drag, locked, nowUtc, selectOnly } from '../lib/organize.svelte';
  import { select } from '../lib/selection';
  import { wallpaperOf } from '../lib/wallpapers';
  import Sprite from './Sprite.svelte';

  const LONG_PRESS_MS = 500, SLOP_PX = 10, COLS = 6, SLOTS = 30;

  const box = $derived(ui.box);
  const mons = $derived(bank.monsIn(box));
  const wp = $derived(wallpaperOf(bank.boxes, box));
  // Each slot's living dex number (null outside the region), and whether a Pokémon of another species sits in it.
  const dex = $derived.by(() => {
    const start = bank.settings.livingDexStartBox, max = names.maxSpecies ?? FALLBACK_MAX_SPECIES;
    return mons.map((_, s) => slotSpecies(start, box, s, max));
  });
  const off = (s: number) => { const m = mons[s], n = dex[s]; return !!m && n !== null && m.value.info.species !== n; };
  const occupied = (s: number) => !!mons[s];
  const several = $derived([...ui.selected].filter(occupied).length > 1);
  // Touch screens long-press to select; only a fine pointer drags (a long-press would start a drag otherwise).
  const fine = matchMedia('(pointer: fine)').matches;
  const canDrag = $derived(fine && !locked());

  let grid: HTMLDivElement;
  let dropSlot = $state<number | null>(null);
  let press: { timer: ReturnType<typeof setTimeout>; x: number; y: number } | null = null;
  let pressed = false;

  function apply(slot: number, mods: { shift: boolean; ctrl: boolean }) {
    const next = select(ui.selected, ui.anchor, slot, occupied, mods);
    ui.selected = next.selected;
    ui.anchor = next.anchor;
  }

  function click(e: MouseEvent, slot: number) {
    if (pressed) { pressed = false; return; }
    apply(slot, { shift: e.shiftKey, ctrl: e.ctrlKey || e.metaKey });
  }

  // Long-press (touch): a Ctrl-click, so selecting several works without a keyboard.
  function down(e: PointerEvent, slot: number) {
    pressed = false; // a long-press may not be followed by a click (iOS, Android with the menu prevented)
    if (e.pointerType !== 'touch') return;
    cancelPress();
    press = { x: e.clientX, y: e.clientY, timer: setTimeout(() => { press = null; pressed = true; apply(slot, { shift: false, ctrl: true }); navigator.vibrate?.(10); }, LONG_PRESS_MS) };
  }
  function move(e: PointerEvent) { if (press && Math.hypot(e.clientX - press.x, e.clientY - press.y) > SLOP_PX) cancelPress(); }
  function cancelPress() { if (press) clearTimeout(press.timer); press = null; }
  /** The click that follows a long-press (if any) comes right after pointerup; after that the flag must not linger. */
  function up() { cancelPress(); if (pressed) setTimeout(() => { pressed = false; }, 50); }

  function keydown(e: KeyboardEvent, slot: number) {
    const step = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -COLS, ArrowDown: COLS }[e.key];
    if (step !== undefined) {
      e.preventDefault();
      const to = slot + step;
      if (to < 0 || to >= SLOTS || (Math.abs(step) === 1 && Math.floor(to / COLS) !== Math.floor(slot / COLS))) return;
      apply(to, { shift: false, ctrl: false });
      (grid.querySelector(`[data-slot="${to}"]`) as HTMLButtonElement | null)?.focus();
    } else if (e.key === 'Enter') {
      e.preventDefault();
      apply(slot, { shift: false, ctrl: false });
      (document.querySelector('.insp') as HTMLElement | null)?.focus();
    }
  }

  function dragstart(e: DragEvent, slot: number) {
    const m = mons[slot];
    if (!m || !canDrag) { e.preventDefault(); return; }
    drag.current = { kind: 'mon', id: m.id, slot };
    e.dataTransfer!.effectAllowed = 'move';
    try { e.dataTransfer!.setData('text/plain', m.value.info.nickname); } catch { /* some browsers refuse; the drag still works */ }
  }
  function dragover(e: DragEvent, slot: number) {
    if (drag.current?.kind !== 'mon') return;
    e.preventDefault();
    e.dataTransfer!.dropEffect = 'move';
    dropSlot = slot;
  }
  function dragleave(slot: number) { if (dropSlot === slot) dropSlot = null; }
  function dragend() { drag.current = null; dropSlot = null; }

  function drop(e: DragEvent, to: number) {
    const d = drag.current;
    dragend();
    if (d?.kind !== 'mon') return;
    e.preventDefault();
    if (d.slot === to) return;
    const target = box;
    let mover = '', other = '';
    void act(() => {
      mover = bank.mon(d.id)?.value.info.nickname ?? '';
      other = bank.monsIn(target)[to]?.value.info.nickname ?? '';
      return moveMon(bank, d.id, target, to, nowUtc());
    }, {
      applied: () => { if (ui.box === target) selectOnly([to]); },
      done: (c) => (c.label === 'Swapped' ? `Swapped ${mover} and ${other}` : `Moved ${mover}`),
    });
  }
</script>

<div class="grid" bind:this={grid} role="group" aria-label="{boxLabel(bank.boxes, box)}, 30 slots"
  style:background-color={wp.tone}
  style:background-image={wp.url ? `url("${wp.url}")` : undefined}
  class:art={!!wp.url}>
  {#each mons as m, i (i)}
    {@const sel = ui.selected.has(i)}
    <button
      class="slot" class:has={!!m} class:off={off(i)} class:sel class:drop={dropSlot === i}
      data-slot={i}
      tabindex={i === ui.anchor ? 0 : -1}
      aria-pressed={sel}
      aria-label={slotLabel(i, m ? m.value.info.nickname : null, !m || off(i) ? dex[i] : null, (n) => names.speciesName(n))}
      draggable={m && canDrag ? 'true' : undefined}
      onclick={(e) => click(e, i)}
      onkeydown={(e) => keydown(e, i)}
      onpointerdown={(e) => down(e, i)}
      onpointermove={move}
      onpointerup={up}
      onpointercancel={cancelPress}
      oncontextmenu={(e) => { if (pressed || press) e.preventDefault(); }}
      ondragstart={(e) => dragstart(e, i)}
      ondragover={(e) => dragover(e, i)}
      ondragleave={() => dragleave(i)}
      ondrop={(e) => drop(e, i)}
      ondragend={dragend}
    >
      {#if m}
        {#key m.id}<Sprite info={m.value.info} />{/key}
        {#if m.value.info.shiny}<span class="shiny" aria-hidden="true"></span>{/if}
        {#if off(i)}<span class="offslot" aria-hidden="true">!</span>{/if}
      {:else if dex[i] !== null}
        <span class="dexslot" aria-hidden="true">{dexNo(dex[i]!)}</span>
      {/if}
      {#if several && sel && m}<span class="chk" aria-hidden="true">{@html ICON.check}</span>{/if}
    </button>
  {/each}
</div>
