import { Area, AreaChart, CartesianGrid, XAxis, YAxis } from 'recharts'
import { ChartContainer, ChartTooltip, ChartTooltipContent, type ChartConfig } from '@/components/ui/chart'
import type { SeriesPoint } from '@/types/analytics'

const config = { views: { label: 'Views', color: 'var(--chart-1)' } } satisfies ChartConfig

const fmt = (d: string) =>
  new Date(d + 'T00:00:00Z').toLocaleDateString(undefined, { month: 'short', day: 'numeric', timeZone: 'UTC' })

/** One series: views per day. Likes and comments are in the KPI row. */
export default function TrafficChart({ series }: { series: SeriesPoint[] }) {
  return (
    <section aria-labelledby="views-heading">
      <h2 id="views-heading" className="mb-3 font-serif text-xl font-semibold">Views per day</h2>
      <ChartContainer config={config} className="h-64 w-full">
        <AreaChart data={series} margin={{ left: 0, right: 4, top: 4 }} accessibilityLayer>
          <defs>
            <linearGradient id="fill-views" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="var(--color-views)" stopOpacity={0.18} />
              <stop offset="100%" stopColor="var(--color-views)" stopOpacity={0} />
            </linearGradient>
          </defs>
          <CartesianGrid vertical={false} strokeOpacity={0.6} />
          <XAxis dataKey="date" tickFormatter={fmt} tickLine={false} axisLine={false} minTickGap={32} tickMargin={8} />
          <YAxis allowDecimals={false} tickLine={false} axisLine={false} width={32} />
          <ChartTooltip cursor={{ strokeOpacity: 0.3 }} content={<ChartTooltipContent indicator="line" labelFormatter={(v) => fmt(String(v))} />} />
          <Area dataKey="views" type="monotone" stroke="var(--color-views)" strokeWidth={2} fill="url(#fill-views)" />
        </AreaChart>
      </ChartContainer>
    </section>
  )
}
