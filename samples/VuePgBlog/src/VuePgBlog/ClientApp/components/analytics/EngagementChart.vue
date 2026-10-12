<script setup lang="ts">
import type { SeriesPoint } from '@/types'
import { VisAxis, VisGroupedBar, VisXYContainer } from '@unovis/vue'
import { useMounted } from '@vueuse/core'
import {
  ChartContainer, ChartCrosshair, ChartLegendContent, ChartTooltip, ChartTooltipContent,
  componentToString, type ChartConfig,
} from '@/components/ui/chart'
import { Skeleton } from '@/components/ui/skeleton'

const props = defineProps<{ series: SeriesPoint[] }>()

const config = {
  likes: { label: 'Likes', color: 'var(--chart-1)' },
  comments: { label: 'Comments', color: 'var(--chart-2)' },
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
const tooltip = componentToString(config, ChartTooltipContent, { labelFormatter: label })
</script>

<template>
  <section class="rounded-xl border bg-card">
    <header class="px-5 pt-5">
      <h2 class="text-base font-semibold">Engagement</h2>
      <p class="text-sm text-muted-foreground">Likes and comments per day.</p>
    </header>
    <div class="px-2 pt-4 pb-3 sm:px-4">
      <ChartContainer v-if="mounted" :config="config" class="aspect-auto h-72 w-full" cursor>
        <VisXYContainer :data="series" :margin="{ left: 0, right: 8, top: 4 }" :y-domain="[0, undefined]">
          <VisGroupedBar :x="x" :y="[(d: SeriesPoint) => d.likes, (d: SeriesPoint) => d.comments]" :color="['var(--color-likes)', 'var(--color-comments)']" :rounded-corners="2" />
          <VisAxis type="x" :x="x" :tick-format="tickFormat" :num-ticks="Math.min(series.length, 7)" :grid-line="false" :tick-line="false" :domain-line="false" />
          <VisAxis type="y" :num-ticks="4" :tick-format="(v: number) => (Number.isInteger(v) ? String(v) : '')" :domain-line="false" :tick-line="false" />
          <ChartTooltip />
          <ChartCrosshair :template="tooltip" :color="['var(--color-likes)', 'var(--color-comments)']" />
        </VisXYContainer>
        <ChartLegendContent class="pt-2" />
      </ChartContainer>
      <Skeleton v-else class="h-72 w-full" />
    </div>
  </section>
</template>