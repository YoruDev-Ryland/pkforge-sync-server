<script lang="ts">
  import type { MonDetails } from '../lib/protocol';
  import { judge, plainStats, STAT_NAMES, STAT_SHORT, statColor, typeColor, typeDark } from '../lib/statcolor';
  import InfoTip from './InfoTip.svelte';
  import Radar from './Radar.svelte';

  /** Stats tab: hexagon over the species' plain stats, then base / IV / EV / value. */
  let { d }: { d: MonDetails } = $props();

  const full = $derived(d.stats.length === 6 && d.baseStats.length === 6);
  const cls = $derived(STAT_SHORT.map((_, k) => (k === d.natureUp ? 'up' : k === d.natureDown ? 'down' : '')));
  const sum = (a: number[]) => a.reduce((x, v) => x + v, 0);
  const top = $derived(Math.max(...d.stats, 1) * 1.08);
  const plain = $derived(plainStats(d.baseStats, d.level));
  const modern = $derived(d.generation >= 3);
  const evTotal = $derived(sum(d.evs));
  const mark = (c: string) => (c === 'up' ? ' ▲' : c === 'down' ? ' ▼' : '');
  const statTip = $derived(`Base bars run from red (low) to teal (high). IVs of ${d.ivMax} are green and 0 is red${modern ? "; hover an IV for the game's judge rating" : ''}.${d.natureUp != null ? ' ▲ and ▼ mark the stats the nature raises and lowers.' : ''}`);
</script>

{#if full}
  <div class="sec">
    <Radar labels={STAT_SHORT} values={d.stats} max={top} under={plain} {cls} label="Stats at this level" />
    <div class="legend"><span><i style="background:var(--accent)"></i>This Pokémon</span><span><i style="background:var(--text-3);opacity:.5"></i>Species at Lv. {d.level}, no IVs/EVs</span></div>
  </div>
  <div class="sec">
    <table class="stab">
      <thead><tr><th><span class="hrow">Stat<InfoTip text={statTip} /></span></th><th style="text-align:left;padding-left:8px">Base</th><th>IV</th><th>EV</th><th>Value</th></tr></thead>
      <tbody>
        {#each STAT_SHORT as s, k (s)}
          <tr>
            <td class={cls[k]} title={STAT_NAMES[k]}>{s}{mark(cls[k])}</td>
            <td class="bar"><div style="display:flex;align-items:center;gap:6px"><span class="b" style:width="{Math.min(100, Math.round((d.baseStats[k] / 180) * 100))}%" style:background={statColor(d.baseStats[k])}></span><span style="font-size:.74rem;color:var(--text-3)">{d.baseStats[k]}</span></div></td>
            <td class={d.ivs[k] === d.ivMax ? 'iv31' : d.ivs[k] === 0 ? 'iv0' : ''} title={modern ? judge(d.ivs[k]) : undefined}>{d.ivs[k]}</td>
            <td>{d.evs[k]}</td>
            <td><b>{d.stats[k]}</b></td>
          </tr>
        {/each}
      </tbody>
      <tfoot><tr><td>Total</td><td class="bar" style="font-size:.78rem;color:var(--text-3)">{sum(d.baseStats)}</td><td>{sum(d.ivs)}</td><td>{evTotal}</td><td>{sum(d.stats)}</td></tr></tfoot>
    </table>
  </div>
{/if}
{#if modern}
  <div class="sec">
    <h4><span class="hrow">Effort values<InfoTip text="Earned from battles and items. A Pokémon can hold 510 in total and 252 in one stat; every 4 points adds 1 to that stat at level 100." /></span><span class="mono">{evTotal} / 510</span></h4>
    <div class="meter ev"><i style:width="{Math.min(100, Math.round((evTotal / 510) * 100))}%"></i></div>
  </div>
{/if}
{#if d.hiddenPower}
  <div class="sec">
    <h4>Hidden Power</h4>
    <div style="display:flex;gap:8px;align-items:center"><span class="chip t" class:dk={typeDark(d.hiddenPower.type)} style:background={typeColor(d.hiddenPower.type)}>{d.hiddenPower.type}</span><span style="font-size:.86rem;color:var(--text-2)">Power {d.hiddenPower.power}, from IVs</span></div>
  </div>
{/if}
