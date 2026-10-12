<script setup lang="ts">
import type { SeriesPoint } from '@/types'
import { VisArea, VisAxis, VisLine, VisXYContainer } from '@unovis/vue'
import { useMounted } from '@vueuse/core'
import {
  ChartContainer, ChartCrosshair, ChartLegendContent, ChartTooltip, ChartTooltipContent,
  componentToString, type ChartConfig,
} from '@/components/ui/chart'
import { Skeleton } from '@/components/ui/skeleton'

const props = defineProps<{ series: SeriesPoint[] }>()

const config = {
  views: { label: 'Views', color: 'var(--chart-1)' },
  uniqueViewers: { label: 'Unique viewers', color: 'var(--chart-2)' },
} satisfies ChartConfig

// Unovis needs the DOM: render the chart only after mount.
const mounted = useMounted()

const fmt = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' })
const label = (i: number | Date) => {
  i = Number(i)
  const p = props.series[Math.round(i)]
  return p ? fmt.format(new Date(p.date + 'T00:00:00')) : ''
}
const x = (_: SeriesPoint, i: number) => i
const tickFormat = (i: number) => (Number.isInteger(i) ? label(i) : '')
const defs = '<linearGradient id="fill-views" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stop-color="var(--chart-1)" stop-opacity="0.28"/><stop offset="100%" stop-color="var(--chart-1)" stop-opacity="0"/></linearGradient>'
const tooltip = componentToString(config, ChartTooltipContent, { labelFormatter: label })
</script>

<template>
  <section class="rounded-xl border bg-card">
    <header class="px-5 pt-5">
      <h2 class="text-base font-semibold">Views over time</h2>
      <p class="text-sm text-muted-foreground">Views and unique viewers per day.</p>
    </header>
    <div class="px-2 pt-4 pb-3 sm:px-4">
      <ChartContainer v-if="mounted" :config="config" class="aspect-auto h-72 w-full" cursor>
        <VisXYContainer :data="series" :margin="{ left: 0, right: 8, top: 4 }" :y-domain="[0, undefined]" :svg-defs="defs">
          <VisArea :x="x" :y="(d: SeriesPoint) => d.views" color="url(#fill-views)" :opacity="1" />
          <VisLine :x="x" :y="(d: SeriesPoint) => d.uniqueViewers" color="var(--color-uniqueViewers)" :line-width="1.5" />
          <VisLine :x="x" :y="(d: SeriesPoint) => d.views" color="var(--color-views)" :line-width="2" />
          <VisAxis type="x" :x="x" :tick-format="tickFormat" :num-ticks="Math.min(series.length, 7)" :grid-line="false" :tick-line="false" :domain-line="false" />
          <VisAxis type="y" :num-ticks="4" :tick-format="(v: number) => (Number.isInteger(v) ? String(v) : '')" :domain-line="false" :tick-line="false" />
          <ChartTooltip />
          <ChartCrosshair :template="tooltip" :color="['var(--color-views)', 'var(--color-uniqueViewers)']" />
        </VisXYContainer>
        <ChartLegendContent class="pt-2" />
      </ChartContainer>
      <Skeleton v-else class="h-72 w-full" />
    </div>
  </section>
</template>