import { Deferred, Link } from '@inertiajs/react'
import { BarChart3 } from 'lucide-react'
import KpiCards from '@/components/analytics/KpiCards'
import RangeTabs from '@/components/analytics/RangeTabs'
import TrafficChart from '@/components/analytics/TrafficChart'
import { EmptyState } from '@/components/EmptyState'
import { PageHeader } from '@/components/PageHeader'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import AppLayout from '@/layouts/AppLayout'
import type { Range, SeriesPoint, Summary, TopPost } from '@/types/analytics'

interface Props {
  range: Range
  summary?: Summary
  series?: SeriesPoint[]
  topPosts?: TopPost[]
}

function TopPosts({ posts }: { posts: TopPost[] }) {
  const max = Math.max(1, ...posts.map((p) => p.views))
  return (
    <section className="rounded-xl border bg-card">
      <header className="px-5 pt-5 pb-3">
        <h2 className="text-base font-semibold">Top posts</h2>
        <p className="text-sm text-muted-foreground">Ranked by views in this period.</p>
      </header>
      {posts.length === 0 ? (
        <p className="border-t px-5 py-8 text-center text-sm text-muted-foreground">No views in this period.</p>
      ) : (
        <ol className="divide-y border-t">
          {posts.map((p, i) => (
            <li
              key={p.id}
              className="grid grid-cols-[1.5rem_1fr_auto] items-center gap-x-3 px-5 py-3 transition-colors hover:bg-muted/50 sm:grid-cols-[1.5rem_1fr_9rem_3.5rem_3.5rem]"
            >
              <span className="text-sm tabular-nums text-muted-foreground">{i + 1}</span>
              <Link href={`/posts/${p.id}/analytics`} className="min-w-0 truncate font-serif text-[17px] font-medium underline-offset-4 hover:underline">
                {p.title}
              </Link>
              <div className="flex items-center justify-end gap-2.5">
                <span className="hidden h-1 flex-1 overflow-hidden rounded-full bg-muted sm:block" aria-hidden>
                  <span className="block h-full rounded-full bg-chart-1" style={{ width: `${(p.views / max) * 100}%` }} />
                </span>
                <span className="w-12 text-right text-sm font-medium tabular-nums">
                  {p.views.toLocaleString()}
                  <span className="sr-only"> views</span>
                </span>
              </div>
              <span className="hidden text-right text-sm tabular-nums text-muted-foreground sm:block">
                {p.likes.toLocaleString()}
                <span className="sr-only"> likes</span>
              </span>
              <span className="hidden text-right text-sm tabular-nums text-muted-foreground sm:block">
                {p.comments.toLocaleString()}
                <span className="sr-only"> comments</span>
              </span>
            </li>
          ))}
        </ol>
      )}
    </section>
  )
}

export default function AnalyticsIndex({ range, summary, series, topPosts }: Props) {
  const empty = summary?.posts === 0
  return (
    <AppLayout title="Analytics">
      <div className="flex flex-col gap-6">
        <PageHeader
          title="Analytics"
          description="How your posts are doing."
          actions={<RangeTabs range={range} only={['summary', 'series', 'topPosts']} />}
        />
        {empty ? (
          <EmptyState
            icon={BarChart3}
            title="No stats yet"
            description="Publish a post and share it to see stats here."
            action={<Button asChild className="rounded-full"><Link href="/posts/create">Write your first post</Link></Button>}
          />
        ) : (
          <>
            <KpiCards summary={summary} />
            <Deferred
              data={['series', 'topPosts']}
              fallback={
                <div className="flex flex-col gap-6">
                  <Skeleton className="h-[26rem] rounded-xl" />
                  <Skeleton className="h-64 rounded-xl" />
                </div>
              }
            >
              <div className="flex flex-col gap-6">
                {series && <TrafficChart series={series} />}
                {topPosts && <TopPosts posts={topPosts} />}
              </div>
            </Deferred>
          </>
        )}
      </div>
    </AppLayout>
  )
}
