<script module lang="ts">
  /** Points of a radar polygon: point i at angle -90° + i·360°/n, distance r·value/max (clamped to 4%–100%). */
  export function radarPoints(values: number[], max: number, cx: number, cy: number, r: number): [number, number][] {
    const n = values.length;
    return values.map((v, i) => {
      const f = Math.max(0.04, Math.min(1, max > 0 ? v / max : 0));
      const a = -Math.PI / 2 + (i * 2 * Math.PI) / n;
      return [cx + Math.cos(a) * r * f, cy + Math.sin(a) * r * f];
    });
  }
</script>

<script lang="ts">
  /** A fixed hexagon (or pentagon) grid, labels and values outside each point. */
  let { labels, values, max, under, cls, color = 'var(--accent)', label = 'Chart' }: {
    labels: string[]; values: number[]; max: number; under?: number[]; cls?: string[]; color?: string; label?: string;
  } = $props();

  const W = 260, H = 230, cx = W / 2, cy = H / 2 + 4, R = 74;
  // Grid and label positions (not clamped: labels sit at 124%).
  const at = (i: number, f: number): [number, number] => {
    const a = -Math.PI / 2 + (i * 2 * Math.PI) / labels.length;
    return [cx + Math.cos(a) * R * f, cy + Math.sin(a) * R * f];
  };
  const join = (pts: [number, number][]) => pts.map(([x, y]) => `${x.toFixed(1)},${y.toFixed(1)}`).join(' ');
  const rings = $derived([0.25, 0.5, 0.75, 1].map((f) => ({ f, pts: join(labels.map((_, i) => at(i, f))) })));
  const spokes = $derived(labels.map((_, i) => at(i, 1)));
  const pts = $derived(radarPoints(values, max, cx, cy, R));
  const tags = $derived(labels.map((l, i) => {
    const [x, y] = at(i, 1.24);
    return { l, x, y, anchor: Math.abs(x - cx) < 4 ? 'middle' : x > cx ? 'start' : 'end', c: cls?.[i] ?? '' };
  }));
</script>

<svg class="radar" viewBox="0 0 {W} {H}" role="img" aria-label={label}>
  {#each rings as r (r.f)}<polygon points={r.pts} fill={r.f === 1 ? 'var(--surface-2)' : 'none'} stroke="var(--line)" stroke-width="1" />{/each}
  {#each spokes as [x, y], i (i)}<line x1={cx} y1={cy} x2={x} y2={y} stroke="var(--line)" stroke-width="1" />{/each}
  {#if under}<polygon points={join(radarPoints(under, max, cx, cy, R))} fill="var(--text-3)" fill-opacity=".18" stroke="var(--text-3)" stroke-opacity=".6" stroke-dasharray="3 2" stroke-width="1" />{/if}
  <polygon points={join(pts)} fill={color} fill-opacity=".32" stroke={color} stroke-width="2" stroke-linejoin="round" />
  {#each pts as [x, y], i (i)}<circle cx={x.toFixed(1)} cy={y.toFixed(1)} r="2.6" fill={color} />{/each}
  {#each tags as t, i (i)}
    <text x={t.x.toFixed(1)} y={(t.y - 2).toFixed(1)} text-anchor={t.anchor} class={t.c}>{t.l}{t.c === 'up' ? ' ▲' : t.c === 'down' ? ' ▼' : ''}</text>
    <text x={t.x.toFixed(1)} y={(t.y + 10).toFixed(1)} text-anchor={t.anchor} class="v">{values[i]}</text>
  {/each}
</svg>
