<script lang="ts">
  import { Area, Axis, Crosshair, GroupedBar, Line, Tooltip, XYContainer } from '@unovis/ts'
  import { onMount } from 'svelte'
  import Skeleton from '@/components/ui/skeleton/Skeleton.svelte'
  import { chartTheme } from './chart-utils'
  import type { AnalyticsSeriesPoint } from '@/types'

  /**
   * One metric over time: daily bars (revenue, sessions) or a line (occupancy), with its own
   * y axis so scales never fight. `format` renders axis ticks and the tooltip value.
   */
  let {
    series,
    value,
    label,
    format,
    kind = 'bar',
    color = 'var(--chart-1)',
    max,
    height = 224,
  }: {
    series: AnalyticsSeriesPoint[]
    value: (point: AnalyticsSeriesPoint) => number
    label: string
    format: (n: number) => string
    kind?: 'bar' | 'line'
    color?: string
    /** Fixed top of the y axis (100 for percentages); defaults to the data. */
    max?: number
    height?: number
  } = $props()

  let el: HTMLDivElement | undefined = $state()
  let mounted = $state(false)
  onMount(() => (mounted = true))

  const dayFmt = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' })
  const dayOf = (i: number | Date) => {
    const point = series[Math.round(Number(i))]
    return point ? dayFmt.format(new Date(`${point.date}T00:00:00Z`)) : ''
  }
  const summary = $derived(
    series.length
      ? `${label}: ${format(Math.min(...series.map(value)))} to ${format(Math.max(...series.map(value)))} per day over ${series.length} days`
      : `${label}: no data`,
  )

  $effect(() => {
    if (!mounted || !el || !series.length) return
    const x = (_: AnalyticsSeriesPoint, i: number) => i
    const y = (d: AnalyticsSeriesPoint) => value(d)
    const tooltip = (d: AnalyticsSeriesPoint, i: number | Date) =>
      `<div class="bg-popover text-popover-foreground rounded-lg px-3 py-2 text-sm shadow-pop ring-1 ring-foreground/10"><div class="text-muted-foreground">${dayOf(i)}</div><div class="font-semibold">${label}: ${format(value(d))}</div></div>`
    const components = kind === 'bar'
      ? [new GroupedBar<AnalyticsSeriesPoint>({ x, y, color, roundedCorners: 2, barPadding: 0.25 })]
      : [
          new Area<AnalyticsSeriesPoint>({ x, y, color, opacity: 0.12 }),
          new Line<AnalyticsSeriesPoint>({ x, y, color, lineWidth: 2.5 }),
        ]
    const container = new XYContainer<AnalyticsSeriesPoint>(el, {
      components,
      xAxis: new Axis<AnalyticsSeriesPoint>({ type: 'x', x, tickFormat: (i: number | Date) => (Number.isInteger(i) ? dayOf(i) : ''), numTicks: Math.min(series.length, 6), gridLine: false, tickLine: false, domainLine: false }),
      yAxis: new Axis<AnalyticsSeriesPoint>({ type: 'y', numTicks: 4, tickFormat: (v: number | Date) => format(Number(v)), domainLine: false, tickLine: false }),
      crosshair: new Crosshair<AnalyticsSeriesPoint>({ x, y: [y], template: tooltip, color: [color] }),
      tooltip: new Tooltip({}),
      margin: { left: 0, right: 8, top: 8 },
      yDomain: [0, max],
    }, series)
    return () => container.destroy()
  })
</script>

{#if mounted}
  <div
    class="[&_.tick_text]:!fill-muted-foreground [&_.tick_line]:!stroke-border/50 w-full text-xs"
    style={`${chartTheme};height:${height}px`}
    role="img"
    aria-label={summary}
  >
    <div bind:this={el} class="h-full w-full"></div>
  </div>
{:else}
  <Skeleton class="w-full" style={`height:${height}px`} />
{/if}
