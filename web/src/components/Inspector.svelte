<script lang="ts">
  import { untrack } from 'svelte';
  import { bank, names, ui } from '../lib/app.svelte';
  import type { MonEntry } from '../lib/bank.svelte';
  import { details } from '../lib/details.svelte';
  import { ICON } from '../lib/icons';
  import { exportName, extOf } from '../lib/download';
  import { dexNo, FALLBACK_MAX_SPECIES, misplaced, offSlotNote, slotSpecies } from '../lib/livingdex';
  import { askRelease, locked, lockedTitle } from '../lib/organize.svelte';
  import { ballUrl } from '../lib/sprites';
  import { typeColor, typeDark } from '../lib/statcolor';
  import { wallpaperOf } from '../lib/wallpapers';
  import InfoTab from './InfoTab.svelte';
  import InfoTip from './InfoTip.svelte';
  import MovesTab from './MovesTab.svelte';
  import PickList from './PickList.svelte';
  import RibbonsTab from './RibbonsTab.svelte';
  import Sprite from './Sprite.svelte';
  import StatsTab from './StatsTab.svelte';

  /** The Pokémon inspector. `onDownload` is the single-Pokémon download; without it there's no Download button. */
  let { onDownload }: { onDownload?: (mon: MonEntry, pid: string | null) => void } = $props();

  const box = $derived(ui.box);
  const mons = $derived(bank.monsIn(box));
  const picked = $derived([...ui.selected].filter((s) => mons[s]).sort((a, b) => a - b));
  const slot = $derived(picked.length === 1 ? picked[0] : ui.selected.has(ui.anchor) ? ui.anchor : [...ui.selected][0]);
  const mon = $derived(picked.length === 1 ? mons[picked[0]] : undefined);
  const wp = $derived(wallpaperOf(bank.boxes, box));
  // Keyed on the Pokémon and its file only: a Bank change that leaves both alone must not ask again
  // (a failed request is retried when the selection comes back to this Pokémon, not on every poll).
  const detailsKey = $derived(mon ? `${mon.id}\n${mon.value.blob}` : '');
  const entry = $derived.by(() => {
    if (!detailsKey) return undefined;
    const [id, blob] = detailsKey.split('\n');
    return untrack(() => details.get(id, blob));
  });
  const d = $derived(entry?.state === 'ready' && entry.data?.available ? entry.data : undefined);
  const unreadable = $derived(entry?.state === 'failed' || (entry?.state === 'ready' && !entry.data?.available));
  const info = $derived(mon?.value.info);
  const maxSpecies = $derived(names.maxSpecies ?? FALLBACK_MAX_SPECIES);
  // A Pokémon sitting in another species' living dex slot; an empty slot's own living dex number.
  const off = $derived(mon && info ? misplaced(bank.settings.livingDexStartBox, box, mon.value.slot, info.species, maxSpecies) : null);
  const emptyDex = $derived(slotSpecies(bank.settings.livingDexStartBox, box, slot ?? 0, maxSpecies));
  const speciesName = (n: number) => names.speciesName(n);

  const gen = $derived(d?.generation ?? info?.generation ?? 0);
  const tabs = $derived([['info', 'Info'], ['stats', 'Stats'], ['moves', 'Moves'], ['ribbons', (d ? d.contest : gen >= 3 && gen <= 4) ? 'Contest' : 'Ribbons']] as const);
  // The same name and extension the download is saved under.
  const ext = $derived(info ? extOf(info) : '');
  const fileName = $derived(info ? exportName(info, d?.pid ?? null, ext) : '');

  let tabButtons: HTMLButtonElement[] = $state([]);
  function tabKey(e: KeyboardEvent, i: number) {
    const step = e.key === 'ArrowRight' ? 1 : e.key === 'ArrowLeft' ? -1 : 0;
    if (!step) return;
    e.preventDefault();
    const next = (i + step + tabs.length) % tabs.length;
    ui.inspectorTab = tabs[next][0];
    tabButtons[next]?.focus();
  }
</script>

<aside class="insp" aria-label="Pokémon" tabindex="-1">
  {#if picked.length > 1}
    <PickList slots={picked} />
  {:else if !mon || !info}
    <div class="emptyinsp">
      <h3 class="hrow">Empty slot<InfoTip text="Drag a Pokémon here to move it. New Pokémon come into the Bank from the app." /></h3>
      <span>Box {box + 1}, slot {(slot ?? 0) + 1}</span>
      {#if emptyDex !== null}<span class="dexfor">Living dex slot for {[dexNo(emptyDex), speciesName(emptyDex)].filter(Boolean).join(' ')}</span>{/if}
    </div>
  {:else}
    <div class="ihead">
      <div class="art" class:pic={!!wp.url} style:background-color={wp.tone} style:background-image={wp.url ? `url("${wp.url}")` : undefined}>
        <Sprite {info} />
        {#if d && d.ballId > 0}<span class="ball" title={d.ball ?? undefined}><img src={ballUrl(d.ballId)} alt={d.ball ?? ''} /></span>{/if}
      </div>
      <div style="min-width:0">
        <h3>{(d?.isNicknamed && d.nickname) || info.nickname}{#if d && d.generation >= 2}{#if d.gender === 'male'}<span class="g m" title="Male">♂</span>{:else if d.gender === 'female'}<span class="g f" title="Female">♀</span>{:else}<span class="g u" title="Genderless">⚲</span>{/if}{/if}</h3>
        <div class="sp">{d?.speciesName ?? names.species(info.species)}{d?.formName ? ` (${d.formName})` : ''} · No. {String(info.species).padStart(3, '0')} · Lv. {d?.level ?? info.level}</div>
        <div class="chips">
          {#each d?.types ?? [] as t (t)}<span class="chip t" class:dk={typeDark(t)} style:background={typeColor(t)}>{t}</span>{/each}
          {#if d?.shiny ?? info.shiny}<span class="chip gold">★ Shiny</span>{/if}
          {#if d?.isEgg}<span class="chip">Egg</span>{/if}
          {#if d?.pokerus === 'infected'}<span class="chip warn">Pokérus</span>{:else if d?.pokerus === 'cured'}<span class="chip">Pokérus cured</span>{/if}
          {#if d?.legal === true}<span class="chip ok" title="PKHeX legality check">✓ Legal</span>
          {:else if d?.legal === false}<span class="chip warn" title={['PKHeX legality check', ...d.legalityNotes].join('\n')}>Not legal</span>{/if}
        </div>
      </div>
      {#if off}<p class="offnote" role="note">{@html ICON.info}<span>{offSlotNote(info.species, off.expected, speciesName)}</span></p>{/if}
    </div>
    {#if unreadable}
      <div class="ibody">
        <p class="note unavailable">Full details aren't available for this file.</p>
        <dl class="facts">
          <dt>Species</dt><dd>{names.species(info.species)}</dd>
          <dt>Nickname</dt><dd>{info.nickname}</dd>
          <dt>Level</dt><dd>{info.level}</dd>
          {#if info.sourceName}<dt>From</dt><dd>{info.sourceName}</dd>{/if}
          {#if info.heldItem}<dt>Held item</dt><dd>{names.item(info.heldItem) || `#${info.heldItem}`}</dd>{/if}
          <dt>In the Bank</dt><dd>Box {box + 1}, slot {mon.value.slot + 1}</dd>
        </dl>
      </div>
    {:else}
      <div class="itabs" role="tablist" aria-label="Summary">
        {#each tabs as [id, label], i (id)}
          <button role="tab" id="itab-{id}" aria-selected={ui.inspectorTab === id} aria-controls="ipanel" tabindex={ui.inspectorTab === id ? 0 : -1}
            class:on={ui.inspectorTab === id} bind:this={tabButtons[i]} onclick={() => (ui.inspectorTab = id)} onkeydown={(e) => tabKey(e, i)}>{label}</button>
        {/each}
      </div>
      <div class="ibody" id="ipanel" role="tabpanel" aria-labelledby="itab-{ui.inspectorTab}" aria-busy={!d}>
        {#if !d}
          <div class="skel" aria-label="Loading"><i></i><i></i><i></i><i></i><i></i></div>
        {:else if ui.inspectorTab === 'stats'}<StatsTab {d} />
        {:else if ui.inspectorTab === 'moves'}<MovesTab {d} />
        {:else if ui.inspectorTab === 'ribbons'}<RibbonsTab {d} />
        {:else}<InfoTab {d} {mon} />{/if}
      </div>
    {/if}
    <div class="actions">
      <div class="row">
        {#if onDownload}<button class="btn primary" onclick={() => onDownload(mon, d?.pid ?? null)}>{@html ICON.down}Download .{ext}</button>{/if}
        <button class="btn danger" disabled={locked()} title={lockedTitle()} onclick={() => askRelease([mon.id])}>Release</button>
      </div>
      {#if onDownload}<span class="file">{fileName}</span>{/if}
    </div>
  {/if}
</aside>
