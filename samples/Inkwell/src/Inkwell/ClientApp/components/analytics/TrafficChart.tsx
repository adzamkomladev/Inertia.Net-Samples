import { Area, AreaChart, CartesianGrid, XAxis, YAxis } from 'recharts'
import { ChartContainer, ChartLegend, ChartLegendContent, ChartTooltip, ChartTooltipContent, type ChartConfig } from '@/components/ui/chart'
import type { SeriesPoint } from '@/types/analytics'

const config = {
  views: { label: 'Views', color: 'var(--chart-1)' },
  likes: { label: 'Likes', color: 'var(--chart-2)' },
  comments: { label: 'Comments', color: 'var(--chart-3)' },
} satisfies ChartConfig

const fmt = (d: string) =>
  new Date(d + 'T00:00:00Z').toLocaleDateString(undefined, { month: 'short', day: 'numeric', timeZone: 'UTC' })

export default function TrafficChart({ series }: { series: SeriesPoint[] }) {
  return (
    <section className="rounded-xl border bg-card">
      <header className="px-5 pt-5">
        <h2 className="text-base font-semibold">Activity</h2>
        <p className="text-sm text-muted-foreground">Views, likes and comments per day.</p>
      </header>
      <div className="px-2 pt-4 pb-3 sm:px-4">
        <ChartContainer config={config} className="h-72 w-full">
          <AreaChart data={series} margin={{ left: 0, right: 8, top: 4 }}>
            <defs>
              <linearGradient id="fill-views" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor="var(--color-views)" stopOpacity={0.28} />
                <stop offset="100%" stopColor="var(--color-views)" stopOpacity={0} />
              </linearGradient>
            </defs>
            <CartesianGrid vertical={false} strokeOpacity={0.5} strokeDasharray="3 3" />
            <XAxis dataKey="date" tickFormatter={fmt} tickLine={false} axisLine={false} minTickGap={32} tickMargin={8} />
            <YAxis allowDecimals={false} tickLine={false} axisLine={false} width={32} />
            <ChartTooltip cursor={{ strokeOpacity: 0.3 }} content={<ChartTooltipContent indicator="line" labelFormatter={(v) => fmt(String(v))} />} />
            <ChartLegend content={<ChartLegendContent />} />
            <Area dataKey="comments" type="monotone" stroke="var(--color-comments)" strokeWidth={1.5} fill="none" />
            <Area dataKey="likes" type="monotone" stroke="var(--color-likes)" strokeWidth={1.5} fill="none" />
            <Area dataKey="views" type="monotone" stroke="var(--color-views)" strokeWidth={2} fill="url(#fill-views)" />
          </AreaChart>
        </ChartContainer>
      </div>
    </section>
  )
}
