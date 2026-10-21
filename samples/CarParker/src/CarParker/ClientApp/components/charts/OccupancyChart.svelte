<script lang="ts">
  import { Axis, Crosshair, Line, Tooltip, XYContainer } from '@unovis/ts'
  import { onMount } from 'svelte'
  import Skeleton from '@/components/ui/skeleton/Skeleton.svelte'
  import { chartSurfaceClass, chartTheme, tooltipHtml, type ChartConfig } from './chart-utils'
  import type { AnalyticsSeriesPoint } from '@/types'

  /** Average deck occupancy percentage per day (0–100). */
  let { series }: { series: AnalyticsSeriesPoint[] } = $props()

  const config = {
    occupancyPct: { label: 'Occupancy %', color: 'var(--chart-1)' },
  } satisfies ChartConfig

  let el: HTMLDivElement | undefined = $state()
  let mounted = $state(false)
  onMount(() => (mounted = true))

  const fmt = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' })
  const label = (i: number | Date) => {
    const point = series[Math.round(Number(i))]
    return point ? fmt.format(new Date(point.date + 'T00:00:00')) : ''
  }
  const theme = `${chartTheme};--color-occupancyPct:var(--chart-1)`

  $effect(() => {
    if (!mounted || !el) return
    const x = (_: AnalyticsSeriesPoint, i: number) => i
    const tickFormat = (i: number | Date) => (Number.isInteger(i) ? label(i) : '')
    const container = new XYContainer<AnalyticsSeriesPoint>(el, {
      components: [
        new Line<AnalyticsSeriesPoint>({ x, y: (d: AnalyticsSeriesPoint) => d.occupancyPct, color: 'var(--color-occupancyPct)', lineWidth: 2 }),
      ],
      xAxis: new Axis<AnalyticsSeriesPoint>({ type: 'x', x, tickFormat, numTicks: Math.min(series.length, 7), gridLine: false, tickLine: false, domainLine: false }),
      yAxis: new Axis<AnalyticsSeriesPoint>({ type: 'y', numTicks: 4, tickFormat: (v: number | Date) => (Number.isInteger(v) ? `${v}%` : ''), domainLine: false, tickLine: false }),
      crosshair: new Crosshair<AnalyticsSeriesPoint>({ template: tooltipHtml(config, label), color: ['var(--color-occupancyPct)'] }),
      tooltip: new Tooltip({}),
      margin: { left: 0, right: 8, top: 4 },
      yDomain: [0, 100],
    }, series)
    return () => container.destroy()
  })
</script>

<div class="px-2 pt-4 pb-3 sm:px-4">
  {#if mounted}
    <div class={chartSurfaceClass} style={theme}>
      <div bind:this={el} class="h-full w-full"></div>
    </div>
  {:else}
    <Skeleton class="h-72 w-full" />
  {/if}
</div>
