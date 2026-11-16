import { useMemo } from 'react'
import { Bar, BarChart, CartesianGrid, XAxis, YAxis } from 'recharts'
import { ChartContainer, ChartTooltip, ChartTooltipContent, type ChartConfig } from '@/components/ui/chart'
import type { SeriesPoint } from '@/types/analytics'

const config = { views: { label: 'Views', color: 'var(--chart-1)' } } satisfies ChartConfig

const fmt = (d: string) =>
  new Date(d + 'T00:00:00Z').toLocaleDateString(undefined, { month: 'short', day: 'numeric', timeZone: 'UTC' })

/** Fills missing days with zero from the first day through today (UTC) so quiet days read as quiet. */
function fillDays(series: SeriesPoint[]): SeriesPoint[] {
  if (series.length === 0) return series
  const byDate = new Map(series.map(p => [p.date, p]))
  const today = new Date().toISOString().slice(0, 10)
  const first = series[0].date
  const last = series[series.length - 1].date > today ? series[series.length - 1].date : today
  const out: SeriesPoint[] = []
  for (let t = new Date(first + 'T00:00:00Z'); ; t = new Date(t.getTime() + 86_400_000)) {
    const d = t.toISOString().slice(0, 10)
    if (d > last) break
    out.push(byDate.get(d) ?? { date: d, views: 0, likes: 0, comments: 0 })
  }
  return out
}

/** One series: views per day. Likes and comments are in the KPI row. */
export default function TrafficChart({ series: raw }: { series: SeriesPoint[] }) {
  const series = useMemo(() => fillDays(raw), [raw])
  const total = series.reduce((sum, p) => sum + p.views, 0)
  return (
    <section aria-labelledby="views-heading" className="rounded-2xl border bg-card p-4 sm:p-6">
      <div className="mb-4 flex items-baseline justify-between gap-3">
        <h2 id="views-heading" className="font-serif text-xl font-semibold">Views per day</h2>
        <p className="text-sm text-muted-foreground tabular-nums">{total.toLocaleString()} total</p>
      </div>
      <div className="relative">
      {total === 0 && (
        <p className="absolute inset-0 z-10 flex items-center justify-center text-center text-sm text-muted-foreground">
          <span className="rounded-full border bg-background/90 px-4 py-2 shadow-sm backdrop-blur">No views in this range yet. Share a post to get readers.</span>
        </p>
      )}
      <ChartContainer config={config} className="h-64 w-full">
        <BarChart data={series} margin={{ left: 0, right: 4, top: 4 }} accessibilityLayer>
          <CartesianGrid vertical={false} strokeOpacity={0.6} />
          <XAxis dataKey="date" tickFormatter={fmt} tickLine={false} axisLine={false} minTickGap={32} tickMargin={8} />
          <YAxis allowDecimals={false} tickLine={false} axisLine={false} width={32} />
          <ChartTooltip cursor={{ fillOpacity: 0.08 }} content={<ChartTooltipContent indicator="line" labelFormatter={(v) => fmt(String(v))} />} />
          <Bar dataKey="views" fill="var(--color-views)" radius={[3, 3, 0, 0]} maxBarSize={28} />
        </BarChart>
      </ChartContainer>
      </div>
    </section>
  )
}
