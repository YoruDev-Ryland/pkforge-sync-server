<script lang="ts">
  import type { MonEntry } from '../lib/bank.svelte';
  import { heart } from '../lib/icons';
  import type { MonDetails } from '../lib/protocol';
  import { calendarDate, STAT_SHORT, typeColor, typeDark } from '../lib/statcolor';
  import InfoTip from './InfoTip.svelte';

  /** The Info tab. Rows a format doesn't have are left out. */
  let { d, mon }: { d: MonDetails; mon: MonEntry } = $props();

  const expPct = $derived(d.expNextLevel ? Math.max(0, Math.min(100, Math.round(((d.exp - d.expThisLevel) / (d.expNextLevel - d.expThisLevel)) * 100))) : 100);
  const hearts = $derived(Math.round((d.friendship / 255) * 5));
  const met = $derived(d.met);
  const metDate = $derived(calendarDate(met?.date));
  // PKHeX names an unset location with a row of dashes, or "(None)" in Gen 2.
  const place = (p: string | null | undefined) => (p && /[^\s\-–—]/.test(p) && !/^\(none\)$/i.test(p) ? p : null);
  const metLine = $derived.by(() => {
    if (!met) return null;
    const at = place(met.location), lv = !met.hatched && met.level != null ? met.level : null;
    const where = at ? (lv != null ? `${at} at Lv. ${lv}` : at) : lv != null ? `Lv. ${lv}` : null;
    return where ? where + (metDate ? `, ${metDate}` : '') : metDate;
  });
  const eggPlace = $derived(place(met?.eggLocation));
  const eggDate = $derived(calendarDate(met?.eggDate));
  const dateOf = (utc: string) => { const t = new Date(utc); return isNaN(+t) ? null : t.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }); };
  const added = $derived(dateOf(mon.value.addedUtc));
  const moved = $derived(dateOf(mon.value.movedUtc));
</script>

<div class="sec"><dl class="facts">
  {#if d.nature}
    <dt>Nature</dt>
    <dd>{d.nature}
      {#if d.statNature}<span style="color:var(--text-3)">· stats as</span> {d.statNature}{/if}
      {#if d.natureUp != null && d.natureDown != null}<span class="up">+{STAT_SHORT[d.natureUp]}</span> <span class="down">−{STAT_SHORT[d.natureDown]}</span>
      {:else}<span style="color:var(--text-3)">(neutral)</span>{/if}
    </dd>
  {/if}
  {#if d.ability}<dt>Ability</dt><dd>{d.ability}</dd>{/if}
  {#if d.generation >= 2}<dt>Held item</dt><dd>{d.heldItem ?? 'None'}</dd>{/if}
  {#if d.ball}<dt>Ball</dt><dd>{d.ball}</dd>{/if}
  {#if d.extra.teraType}<dt>Tera Type</dt><dd><span class="chip t" class:dk={typeDark(d.extra.teraType)} style:background={typeColor(d.extra.teraType)}>{d.extra.teraType}</span></dd>{/if}
  {#if d.extra.dynamaxLevel != null}<dt>Dynamax</dt><dd>Level {d.extra.dynamaxLevel}</dd>{/if}
  {#if d.extra.scale != null}<dt>Size</dt><dd><span class="mono">{d.extra.scale} / 255</span></dd>{/if}
  {#if d.extra.alpha}<dt>Alpha</dt><dd>Yes</dd>{/if}
  {#if d.markings.length}<dt>Markings</dt><dd><span class="marks">{#each d.markings as on, k (k)}<i class:on></i>{/each}</span></dd>{/if}
</dl></div>

{#if d.growthRate}
  <div class="sec">
    <h4><span class="hrow">Experience<InfoTip text="The EXP curve this species levels on: how much EXP each level takes." /></span><span>{d.growthRate}</span></h4>
    <div class="meter exp"><i style:width="{expPct}%"></i></div>
    <div class="meterline"><span class="mono">{d.exp.toLocaleString('en-US')} EXP</span><span>{d.expNextLevel ? `${(d.expNextLevel - d.exp).toLocaleString('en-US')} to Lv. ${d.level + 1}` : 'Max level'}</span></div>
  </div>
{/if}

{#if d.generation >= 2 && !d.isEgg}
  <div class="sec">
    <h4>Friendship <span class="mono">{d.friendship} / 255</span></h4>
    <div class="hearts" role="img" aria-label="{hearts} of 5 hearts">{#each [0, 1, 2, 3, 4] as k (k)}{@html heart(k < hearts)}{/each}</div>
  </div>
{/if}

<div class="sec"><h4>Trainer memo</h4><dl class="facts">
  {#if d.ot}
    <dt>OT</dt><dd><span class="g {d.ot.gender === 'female' ? 'f' : 'm'}">{d.ot.name}</span></dd>
    <dt>ID No.</dt><dd class="mono">{d.ot.tid}{#if d.ot.sid}{' '}<span style="color:var(--text-3)">SID {d.ot.sid}</span>{/if}</dd>
  {/if}
  {#if metLine}<dt>{met?.hatched ? 'Hatched' : 'Met'}</dt><dd>{metLine}</dd>{/if}
  {#if eggPlace}<dt>Egg from</dt><dd>{eggPlace}{eggDate ? `, ${eggDate}` : ''}</dd>{/if}
  {#if d.game}<dt>Game</dt><dd>Pokémon {d.game}</dd>{/if}
  {#if d.language && d.language !== 'None'}<dt>Language</dt><dd>{d.language}</dd>{/if}
  <dt>In the Bank</dt><dd>Box {mon.value.box + 1}, slot {mon.value.slot + 1}</dd>
</dl></div>

<details class="tech"><summary>File details</summary><dl class="facts">
  {#if d.pid}<dt>PID</dt><dd class="mono">{d.pid}</dd>{/if}
  {#if d.encryptionConstant}<dt>Encryption</dt><dd class="mono">{d.encryptionConstant}</dd>{/if}
  <dt>Format</dt><dd>.{d.format} · Gen {d.generation} · {d.size} bytes</dd>
  <dt>Checksum</dt><dd>{#if d.checksumValid}Valid{:else}<span style="color:var(--danger)">Doesn't match</span>{/if}</dd>
  {#if added}<dt>Added</dt><dd>{added}</dd>{/if}
  {#if moved}<dt>Last moved</dt><dd>{moved}</dd>{/if}
</dl></details>
