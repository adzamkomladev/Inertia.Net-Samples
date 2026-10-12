<script lang="ts">
  import { Area, Axis, Crosshair, Line, Tooltip, XYContainer } from '@unovis/ts'
  import { onMount } from 'svelte'
  import Skeleton from '@/components/ui/skeleton/Skeleton.svelte'
  import { chartSurfaceClass, chartTheme, tooltipHtml, type ChartConfig } from './chart-tooltip'
  import type { SeriesPoint } from '@/types'

  let { series }: { series: SeriesPoint[] } = $props()

  const config = {
    views: { label: 'Views', color: 'var(--chart-1)' },
    uniqueViewers: { label: 'Unique viewers', color: 'var(--chart-2)' },
  } satisfies ChartConfig

  let el: HTMLDivElement | undefined = $state()
  // Unovis needs the DOM: render the chart only after mount.
  let mounted = $state(false)
  onMount(() => (mounted = true))

  const fmt = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' })
  const label = (i: number | Date) => {
    const point = series[Math.round(Number(i))]
    return point ? fmt.format(new Date(point.date + 'T00:00:00')) : ''
  }

  $effect(() => {
    if (!mounted || !el) return
    const x = (_: SeriesPoint, i: number) => i
    const tickFormat = (i: number | Date) => (Number.isInteger(i) ? label(i) : '')
    const defs = '<linearGradient id="fill-views" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stop-color="var(--chart-1)" stop-opacity="0.28"/><stop offset="100%" stop-color="var(--chart-1)" stop-opacity="0"/></linearGradient>'
    const container = new XYContainer<SeriesPoint>(el, {
      components: [
        new Area<SeriesPoint>({ x, y: (d: SeriesPoint) => d.views, color: 'url(#fill-views)', opacity: 1 }),
        new Line<SeriesPoint>({ x, y: (d: SeriesPoint) => d.uniqueViewers, color: 'var(--color-uniqueViewers)', lineWidth: 1.5 }),
        new Line<SeriesPoint>({ x, y: (d: SeriesPoint) => d.views, color: 'var(--color-views)', lineWidth: 2 }),
      ],
      xAxis: new Axis<SeriesPoint>({ type: 'x', x, tickFormat, numTicks: Math.min(series.length, 7), gridLine: false, tickLine: false, domainLine: false }),
      yAxis: new Axis<SeriesPoint>({ type: 'y', numTicks: 4, tickFormat: (v: number | Date) => (Number.isInteger(v) ? String(v) : ''), domainLine: false, tickLine: false }),
      crosshair: new Crosshair<SeriesPoint>({ template: tooltipHtml(config, label), color: ['var(--color-views)', 'var(--color-uniqueViewers)'] }),
      tooltip: new Tooltip({}),
      margin: { left: 0, right: 8, top: 4 },
      yDomain: [0, undefined],
      svgDefs: defs,
    }, series)
    return () => container.destroy()
  })

  const theme = `${chartTheme};--color-views:var(--chart-1);--color-uniqueViewers:var(--chart-2)`
</script>

<section class="rounded-xl border bg-card">
  <header class="px-5 pt-5">
    <h2 class="text-base font-semibold">Views over time</h2>
    <p class="text-muted-foreground text-sm">Views and unique viewers per day.</p>
  </header>
  <div class="px-2 pt-4 pb-3 sm:px-4">
    {#if mounted}
      <div class={chartSurfaceClass} style={theme}>
        <div bind:this={el} class="h-full w-full"></div>
      </div>
      <div class="flex items-center justify-center gap-4 pt-3">
        {#each Object.entries(config) as [key, item] (key)}
          <div class="flex items-center gap-1.5">
            <div class="h-2 w-2 shrink-0 rounded-xs" style={`background-color:${item.color}`}></div>
            <span>{item.label}</span>
          </div>
        {/each}
      </div>
    {:else}
      <Skeleton class="h-72 w-full" />
    {/if}
  </div>
</section>
