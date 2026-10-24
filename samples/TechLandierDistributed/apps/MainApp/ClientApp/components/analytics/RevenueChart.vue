<script setup lang="ts">
import { computed } from 'vue'
import { CurveType } from '@unovis/ts'
import { VisArea, VisAxis, VisLine, VisXYContainer } from '@unovis/vue'
import { useMounted } from '@vueuse/core'
import {
  ChartContainer, ChartCrosshair, ChartTooltip, ChartTooltipContent,
  componentToString, type ChartConfig,
} from '@/components/ui/chart'
import { Skeleton } from '@/components/ui/skeleton'
import { moneyWhole } from '@/lib/money'
import type { RevenueSeriesPoint } from '@/types'

const props = withDefaults(defineProps<{ series: RevenueSeriesPoint[]; height?: string }>(), { height: 'h-72' })

const config = {
  revenue: { label: 'Revenue', color: 'var(--chart-1)' },
} satisfies ChartConfig

// Unovis needs the DOM: render the chart only after mount.
const mounted = useMounted()

const fmt = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' })
const label = (i: number | Date) => {
  i = Number(i)
  const p = props.series[Math.round(i)]
  return p ? fmt.format(new Date(p.date + 'T00:00:00')) : ''
}
const x = (_: RevenueSeriesPoint, i: number) => i
const tickFormat = (i: number) => (Number.isInteger(i) ? label(i) : '')
const defs =
  '<linearGradient id="fill-revenue" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stop-color="var(--chart-1)" stop-opacity="0.18"/><stop offset="100%" stop-color="var(--chart-1)" stop-opacity="0"/></linearGradient>'
const tooltip = componentToString(config, ChartTooltipContent, { labelFormatter: label })
const hasData = computed(() => props.series.length > 0)
</script>

<!-- Revenue over time (admin dashboards). -->
<template>
  <ChartContainer v-if="mounted && hasData" :config="config" class="aspect-auto w-full" :class="height">
    <VisXYContainer :data="series" :margin="{ left: 8, right: 8, top: 4 }" :y-domain="[0, undefined]" :svg-defs="defs">
      <VisArea :x="x" :y="(d: RevenueSeriesPoint) => d.revenue" color="url(#fill-revenue)" :opacity="1" :curve-type="CurveType.MonotoneX" />
      <VisLine :x="x" :y="(d: RevenueSeriesPoint) => d.revenue" color="var(--color-revenue)" :line-width="1.75" :curve-type="CurveType.MonotoneX" />
      <VisAxis type="x" :x="x" :tick-format="tickFormat" :num-ticks="Math.min(series.length, 7)" :grid-line="false" :tick-line="false" :domain-line="false" />
      <VisAxis type="y" :num-ticks="4" :tick-format="(v: number) => moneyWhole(v)" :domain-line="false" :tick-line="false" />
      <ChartTooltip />
      <ChartCrosshair :template="tooltip" :y="(d: RevenueSeriesPoint) => d.revenue" :color="['var(--color-revenue)']" />
    </VisXYContainer>
  </ChartContainer>
  <Skeleton v-else class="w-full rounded-xl" :class="height" />
</template>
