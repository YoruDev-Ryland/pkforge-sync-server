<script lang="ts">
  import { bank, names, ui } from '../lib/app.svelte';
  import { boxLabel } from '../lib/bank.svelte';
  import { MAX_NAME, renameBox, setWallpaper } from '../lib/changes';
  import { ICON } from '../lib/icons';
  import { FALLBACK_MAX_SPECIES, rangeText, regionRange } from '../lib/livingdex';
  import { act, locked, lockedTitle } from '../lib/organize.svelte';
  import { router } from '../lib/router.svelte';
  import { wallpaperOf } from '../lib/wallpapers';
  import InfoTip from './InfoTip.svelte';
  import Popover from './Popover.svelte';
  import WallpaperPicker from './WallpaperPicker.svelte';

  /** `covered`: the selection bar lies over this row, so it is hidden from assistive tech and can't be tabbed into. */
  let { covered = false }: { covered?: boolean } = $props();

  const HOW = 'Drag a Pokémon to move it; drop it on another to swap them, or on a box in the sidebar to send it there. <kbd>Shift</kbd>-click selects a range, <kbd>Ctrl</kbd>-click adds or removes one. On touch, long-press to start selecting.';

  const box = $derived(ui.box);
  const name = $derived(boxLabel(bank.boxes, box));
  const count = $derived(bank.monsIn(box).filter(Boolean).length);
  const wp = $derived(wallpaperOf(bank.boxes, box));
  const dex = $derived(regionRange(bank.settings.livingDexStartBox, box, names.maxSpecies ?? FALLBACK_MAX_SPECIES));
  const ro = $derived(locked());

  let renaming = $state(false);
  let draft = $state('');
  let wpOpen = $state(false);
  let input = $state<HTMLInputElement>();

  // Another box: leave rename mode and close the picker.
  $effect(() => { void box; renaming = false; wpOpen = false; });
  $effect(() => { if (renaming) input?.focus(); });
  $effect(() => { if (ro) { renaming = false; wpOpen = false; } });

  const go = (step: number) => router.go(`/bank/${((box + step + bank.boxCount) % bank.boxCount) + 1}`);

  function startRename() { draft = bank.boxes.names?.[box] ?? ''; renaming = true; }
  function cancel() { renaming = false; }
  function save() {
    const target = box, value = draft;
    renaming = false;
    void act(() => renameBox(bank, target, value));
  }
  function key(e: KeyboardEvent) {
    if (e.key === 'Enter') { e.preventDefault(); save(); }
    else if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); cancel(); }
  }
  function choose(id: string, close: () => void) {
    const target = box;
    close();
    void act(() => setWallpaper(bank, target, id));
  }
</script>

<div class="workhead" aria-hidden={covered ? 'true' : undefined} inert={covered}>
  <button class="btn iconbtn" aria-label="Previous box" title="Previous box" onclick={() => go(-1)}>{@html ICON.left}</button>
  <button class="btn iconbtn" aria-label="Next box" title="Next box" onclick={() => go(1)}>{@html ICON.right}</button>
  <div class="title">
    {#if renaming}
      <input class="renamer" bind:this={input} bind:value={draft} maxlength={MAX_NAME} placeholder="Box {String(box + 1).padStart(2, '0')}"
        aria-label="Box name (up to 24 characters, empty for its number)" onkeydown={key} />
    {:else}
      <h2 title={name}>{name}</h2>
      <div class="meta hrow">
        <span class="metatext">Box {box + 1} of {bank.boxCount} · {count}/30{#if dex}{' · '}<span class="tag">Living dex {rangeText(dex)}</span>{/if}</span>
        <InfoTip text={HOW} label="How to organize" />
      </div>
    {/if}
  </div>
  <div class="tools">
    {#if renaming}
      <button class="btn sm primary" onclick={save}>Save</button>
      <button class="btn sm" onclick={cancel}>Cancel</button>
    {:else}
      <button class="btn sm" aria-label="Rename box" title={lockedTitle() ?? 'Rename box'} disabled={ro} onclick={startRename}>{@html ICON.pen}<span class="lbl">Rename</span></button>
      <Popover bind:open={wpOpen} label="Wallpaper">
        {#snippet trigger(toggle, open)}
          <button class="btn sm" aria-expanded={open} aria-haspopup="dialog" aria-label="Wallpaper" title={lockedTitle() ?? 'Wallpaper'} disabled={ro} onclick={toggle}>
            <span class="wpchip" style:background-color={wp.tone} style:background-image={wp.url ? `url("${wp.url}")` : undefined}></span><span class="lbl">Wallpaper</span>
          </button>
        {/snippet}
        {#snippet children(close)}
          <WallpaperPicker current={wp.id} choose={(id) => choose(id, close)} />
        {/snippet}
      </Popover>
    {/if}
  </div>
</div>
