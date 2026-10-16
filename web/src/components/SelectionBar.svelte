<script lang="ts">
  import { bank, ui } from '../lib/app.svelte';
  import { boxLabel } from '../lib/bank.svelte';
  import { moveMany } from '../lib/changes';
  import { ICON } from '../lib/icons';
  import { act, askRelease, fullAsName, locked, lockedTitle, nowUtc, selectOnly } from '../lib/organize.svelte';
  import Popover from './Popover.svelte';
  import BoxPicker from './BoxPicker.svelte';
  import Sprite from './Sprite.svelte';

  /** Lies over the box header while two or more are selected. */
  let { download }: { download?: (ids: string[]) => void } = $props();

  const box = $derived(ui.box);
  const picks = $derived([...ui.selected].sort((a, b) => a - b).map((s) => ({ s, m: bank.monsIn(box)[s] })).filter((p) => !!p.m));
  const n = $derived(picks.length);
  const ro = $derived(locked());
  let moveOpen = $state(false);

  function moveTo(to: number, close: () => void) {
    close();
    const ids = picks.map((p) => p.m!.id), from = box;
    void act(fullAsName(to, () => moveMany(bank, ids, to, nowUtc())), {
      applied: () => { if (ui.box === from) selectOnly([picks[0]?.s ?? ui.anchor]); },
      done: () => `Moved ${ids.length} to ${boxLabel(bank.boxes, to)}`,
    });
  }
</script>

<div class="selbar" role="toolbar" aria-label="{n} Pokémon selected">
  <div class="selcount" title="{n} selected">
    <span class="num">{n}</span>
    <span class="thumbs">{#each picks.slice(0, 4) as p (p.m!.id)}<Sprite info={p.m!.value.info} />{/each}</span>
  </div>
  <Popover bind:open={moveOpen} align="left" label="Move to box" cls="movewrap">
    {#snippet trigger(toggle, open)}
      <button class="btn sm primary" aria-expanded={open} aria-haspopup="dialog" disabled={ro} title={lockedTitle()} onclick={toggle}>Move to…</button>
    {/snippet}
    {#snippet children(close)}
      <BoxPicker from={box} need={n} choose={(to) => moveTo(to, close)} />
    {/snippet}
  </Popover>
  <span class="sep"></span>
  <button class="btn sm dlsel" title="Download as .zip" disabled={!download} onclick={() => download?.(picks.map((p) => p.m!.id))}>{@html ICON.down}<span class="lbl">Download</span></button>
  <button class="btn sm danger" disabled={ro} title={lockedTitle()} onclick={() => askRelease(picks.map((p) => p.m!.id))}>Release</button>
  <button class="btn sm x" aria-label="Clear selection" title="Clear selection" onclick={() => selectOnly([ui.anchor])}>{@html ICON.x}</button>
</div>
