<script lang="ts">
  import { Area, Axis, Crosshair, Line, Tooltip, XYContainer } from '@unovis/ts'
  import { onMount } from 'svelte'
  import Skeleton from '@/components/ui/skeleton/Skeleton.svelte'
  import { chartSurfaceClass, chartTheme, tooltipHtml, type ChartConfig } from './chart-utils'
  import type { AnalyticsSeriesPoint } from '@/types'

  /** Daily revenue (amber area + navy line) and sessions (mint line) for /admin/analytics. */
  let { series, height = 288 }: { series: AnalyticsSeriesPoint[]; height?: number } = $props()

  const config = {
    revenueCents: { label: 'Revenue', color: 'var(--chart-2)' },
    sessions: { label: 'Sessions', color: 'var(--chart-3)' },
    reservations: { label: 'Reservations', color: 'var(--chart-4)' },
  } satisfies ChartConfig

  let el: HTMLDivElement | undefined = $state()
  let mounted = $state(false)
  onMount(() => (mounted = true))

  const fmt = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' })
  const label = (i: number | Date) => {
    const point = series[Math.round(Number(i))]
    return point ? fmt.format(new Date(point.date + 'T00:00:00')) : ''
  }

  const dollars = (value: number) => `$${value.toLocaleString()}`
  const theme = `${chartTheme};--color-revenueCents:var(--chart-2);--color-sessions:var(--chart-3);--color-reservations:var(--chart-4)`

  $effect(() => {
    if (!mounted || !el) return
    const x = (_: AnalyticsSeriesPoint, i: number) => i
    const tickFormat = (i: number | Date) => (Number.isInteger(i) ? label(i) : '')
    const defs = '<linearGradient id="fill-revenue" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stop-color="var(--chart-2)" stop-opacity="0.30"/><stop offset="100%" stop-color="var(--chart-2)" stop-opacity="0"/></linearGradient>'
    const container = new XYContainer<AnalyticsSeriesPoint>(el, {
      components: [
        new Area<AnalyticsSeriesPoint>({ x, y: (d: AnalyticsSeriesPoint) => d.revenueCents, color: 'url(#fill-revenue)', opacity: 1 }),
        new Line<AnalyticsSeriesPoint>({ x, y: (d: AnalyticsSeriesPoint) => d.revenueCents, color: 'var(--color-revenueCents)', lineWidth: 2 }),
        new Line<AnalyticsSeriesPoint>({ x, y: (d: AnalyticsSeriesPoint) => d.sessions, color: 'var(--color-sessions)', lineWidth: 1.5 }),
        new Line<AnalyticsSeriesPoint>({ x, y: (d: AnalyticsSeriesPoint) => d.reservations, color: 'var(--color-reservations)', lineWidth: 1.5 }),
      ],
      xAxis: new Axis<AnalyticsSeriesPoint>({ type: 'x', x, tickFormat, numTicks: Math.min(series.length, 7), gridLine: false, tickLine: false, domainLine: false }),
      yAxis: new Axis<AnalyticsSeriesPoint>({ type: 'y', numTicks: 4, tickFormat: (v: number | Date) => (Number.isInteger(v) ? dollars(Number(v)) : ''), domainLine: false, tickLine: false }),
      crosshair: new Crosshair<AnalyticsSeriesPoint>({ template: tooltipHtml(config, label), color: ['var(--color-revenueCents)', 'var(--color-sessions)', 'var(--color-reservations)'] }),
      tooltip: new Tooltip({}),
      margin: { left: 0, right: 8, top: 4 },
      yDomain: [0, undefined],
      svgDefs: defs,
    }, series)
    return () => container.destroy()
  })
</script>

<div class="px-2 pt-4 pb-3 sm:px-4">
  {#if mounted}
    <div class={chartSurfaceClass} style={`${theme};height:${height}px`}>
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
    <Skeleton class="w-full" style={`height:${height}px`} />
  {/if}
</div>
