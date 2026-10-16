<script lang="ts">
  import { app, bank, names, prefs, toast, ui } from '../lib/app.svelte';
  import { boxLabel } from '../lib/bank.svelte';
  import { addBox, deleteBox, reorderBox, sendToBox } from '../lib/changes';
  import { ICON } from '../lib/icons';
  import { act, drag, fullAsName, locked, lockedTitle, nowUtc } from '../lib/organize.svelte';
  import { FALLBACK_MAX_SPECIES, rangeText, regionRange } from '../lib/livingdex';
  import { key, type MonValue } from '../lib/protocol';
  import { RELEASED_PATH, router } from '../lib/router.svelte';
  import type { Mode } from '../lib/theme';
  import BoxMenu from './BoxMenu.svelte';
  import Mini from './Mini.svelte';
  import StatusBar from './StatusBar.svelte';

  /** `released`: the Bank's Recently released view (the Bank tab stays on, no box row is). */
  let { on }: { on: 'bank' | 'acct' | 'released' } = $props();
  const inBank = $derived(on !== 'acct');

  const NEXT: Record<Mode, Mode> = { system: 'light', light: 'dark', dark: 'system' };
  const MODE_ICON = { system: ICON.auto, light: ICON.sun, dark: ICON.moon };

  const maxSpecies = $derived(names.maxSpecies ?? FALLBACK_MAX_SPECIES);
  const rows = $derived(Array.from({ length: bank.loaded ? bank.boxCount : 0 }, (_, i) => {
    const mons = bank.monsIn(i);
    return {
      i,
      name: boxLabel(bank.boxes, i),
      count: mons.filter(Boolean).length,
      dex: regionRange(bank.settings.livingDexStartBox, i, maxSpecies),
    };
  }));
  const total = $derived(rows.reduce((n, r) => n + r.count, 0));

  const openBox = (i: number) => router.go(`/bank/${i + 1}`);
  const RELEASED = 'released';
  function pick(value: string) { if (value === RELEASED) router.go(RELEASED_PATH); else openBox(Number(value)); }

  // Organizing from the sidebar: rows take dropped Pokémon (sent to the first free slot) and are dragged to reorder.
  const fine = matchMedia('(pointer: fine)').matches;
  const ro = $derived(locked());
  let dropBox = $state<number | null>(null);
  let menu = $state<{ box: number; x: number; y: number } | null>(null);
  let press: ReturnType<typeof setTimeout> | undefined;
  let pressed = false;

  /** The open box keeps showing after the boxes shift (reorder, delete). */
  const follow = (box: number) => {
    if (box === ui.box) return;
    ui.box = box; // set first, so the selection stays: it is still the same box
    router.go(`/bank/${box + 1}`, true);
  };

  function accepts(row: number): boolean {
    const d = drag.current;
    return !!d && (d.kind === 'mon' ? row !== ui.box : row !== d.box);
  }
  function dragstart(e: DragEvent, row: number) {
    if (ro || !fine) { e.preventDefault(); return; }
    drag.current = { kind: 'box', box: row };
    e.dataTransfer!.effectAllowed = 'move';
    try { e.dataTransfer!.setData('text/plain', boxLabel(bank.boxes, row)); } catch { /* the drag still works */ }
  }
  function dragover(e: DragEvent, row: number) {
    if (!accepts(row)) { if (dropBox === row) dropBox = null; return; }
    e.preventDefault();
    e.dataTransfer!.dropEffect = 'move';
    dropBox = row;
  }
  function dragend() { drag.current = null; dropBox = null; }

  function drop(e: DragEvent, row: number) {
    const d = drag.current, ok = accepts(row);
    dragend();
    if (!d || !ok) return;
    e.preventDefault();
    if (d.kind === 'mon') {
      let nick = '';
      void act(fullAsName(row, () => { nick = bank.mon(d.id)?.value.info.nickname ?? ''; return sendToBox(bank, d.id, row, nowUtc()); }), {
        done: (c) => `Sent ${nick} to ${boxLabel(bank.boxes, row)}, slot ${(c.apply.get(key('mon', d.id)) as MonValue).slot + 1}`,
      });
    } else {
      const open = ui.box, from = d.box;
      const moved = open === from ? row : from < open && open <= row ? open - 1 : row <= open && open < from ? open + 1 : open;
      void act(() => reorderBox(bank, from, row, nowUtc()), { applied: () => follow(moved) });
    }
  }

  function add() {
    void act(() => addBox(bank), { applied: () => router.go(`/bank/${bank.boxCount + 1}`) }); // runs before the new count lands
  }

  async function remove(box: number) {
    // Another device may have filled it since the last poll.
    await app.poll();
    if (bank.monsIn(box).some(Boolean)) { toast('Empty the box first'); return; }
    const open = ui.box;
    // `applied` runs before the smaller count lands, hence the - 2.
    void act(() => deleteBox(bank, box, nowUtc()), { applied: () => follow(open > box ? open - 1 : Math.min(open, bank.boxCount - 2)) });
  }

  function openMenu(e: MouseEvent, box: number) {
    e.preventDefault();
    if (ro) return;
    const r = (e.currentTarget as HTMLElement).getBoundingClientRect();
    // From the keyboard (menu key) there's no pointer position: open under the row.
    menu = e.clientX || e.clientY ? { box, x: e.clientX, y: e.clientY } : { box, x: r.left + 12, y: r.bottom };
  }
  function down(e: PointerEvent, box: number) {
    pressed = false; // a long-press may not be followed by a click (iOS, Android with the menu prevented)
    if (e.pointerType !== 'touch' || ro) return;
    clearTimeout(press);
    const { clientX: x, clientY: y } = e;
    press = setTimeout(() => { pressed = true; menu = { box, x, y }; }, 500);
  }
  const unpress = () => clearTimeout(press);
  /** The click that follows a long-press (if any) comes right after pointerup; after that the flag must not linger. */
  const up = () => { unpress(); if (pressed) setTimeout(() => { pressed = false; }, 50); };
  function rowClick(i: number) {
    if (pressed) { pressed = false; return; }
    openBox(i);
  }

  function cycleMode() {
    const mode = NEXT[prefs.mode];
    void prefs.set({ mode });
    toast(`Theme: ${mode}`);
  }
</script>

{#if menu}
  {@const m = menu}
  <BoxMenu x={m.x} y={m.y} empty={!bank.monsIn(m.box).some(Boolean)} last={bank.boxCount <= 1} remove={() => remove(m.box)} close={() => (menu = null)} />
{/if}
<aside class="side">
  <div class="side-top">
    <div class="logo">{@html ICON.ball}PKForge Bank</div>
    <nav class="navlinks" aria-label="Pages">
      <button class:on={inBank} aria-current={inBank ? 'page' : undefined} onclick={() => router.go(`/bank/${ui.box + 1}`)}>Bank</button>
      <button class:on={on === 'acct'} aria-current={on === 'acct' ? 'page' : undefined} onclick={() => router.go('/account')}>Account</button>
    </nav>
    {#if inBank}
      <select class="boxpick" aria-label="Box" value={on === 'released' ? RELEASED : String(ui.box)} onchange={(e) => pick(e.currentTarget.value)}>
        {#each rows as r (r.i)}
          <option value={String(r.i)}>{r.name} · {r.count}/30{r.dex ? ` · ${rangeText(r.dex)}` : ''}</option>
        {/each}
        {#if bank.loaded}<option value={RELEASED}>Recently released</option>{/if}
      </select>
    {/if}
  </div>
  {#if inBank}
    <div class="boxlist">
      {#if bank.loaded}
        <div class="label"><span>Boxes</span><span>{total} Pokémon</span></div>
      {/if}
      {#each rows as r (r.i)}
        <button class="boxrow" class:on={on === 'bank' && r.i === ui.box} class:drop={dropBox === r.i} aria-current={on === 'bank' && r.i === ui.box ? 'true' : undefined} data-box={r.i}
          draggable={fine && !ro ? 'true' : undefined}
          onclick={() => rowClick(r.i)}
          oncontextmenu={(e) => openMenu(e, r.i)}
          onpointerdown={(e) => down(e, r.i)} onpointerup={up} onpointercancel={unpress} onpointerleave={unpress}
          ondragstart={(e) => dragstart(e, r.i)} ondragover={(e) => dragover(e, r.i)} ondragleave={() => { if (dropBox === r.i) dropBox = null; }}
          ondrop={(e) => drop(e, r.i)} ondragend={dragend}>
          <Mini box={r.i} />
          <span style="min-width:0"><span class="nm" title={r.name}>{r.name}</span>{#if r.dex}<span class="sub">Living dex · {rangeText(r.dex)}</span>{/if}</span>
          <span class="ct" aria-label="{r.count} Pokémon">{r.count}</span>
        </button>
      {/each}
      {#if bank.loaded}
        <button class="boxrow addrow" disabled={ro} title={lockedTitle()} onclick={add}>
          <span class="mini addmini" aria-hidden="true">{@html ICON.plus}</span><span class="nm">Add a box</span>
        </button>
      {/if}
    </div>
    {#if bank.loaded}
      <div class="side-rel">
        <button class="boxrow relrow" class:on={on === 'released'} aria-current={on === 'released' ? 'page' : undefined} onclick={() => router.go(RELEASED_PATH)}>
          <span class="mini relmini" aria-hidden="true">{@html ICON.undo}</span><span class="nm">Recently released</span>
        </button>
      </div>
    {/if}
  {:else}
    <div></div>
  {/if}
  <div class="side-foot">
    <StatusBar />
    <button class="modebtn" aria-label="Theme: {prefs.mode}" title="Theme: {prefs.mode}" onclick={cycleMode}>{@html MODE_ICON[prefs.mode]}</button>
  </div>
</aside>
