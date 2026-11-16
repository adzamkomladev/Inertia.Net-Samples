import { Deferred, Link } from '@inertiajs/react'
import { BarChart3 } from 'lucide-react'
import { useState } from 'react'
import KpiCards from '@/components/analytics/KpiCards'
import RangeTabs from '@/components/analytics/RangeTabs'
import TrafficChart from '@/components/analytics/TrafficChart'
import { EmptyState } from '@/components/EmptyState'
import { PageHeader } from '@/components/PageHeader'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import type { Range, SeriesPoint, Summary, TopPost } from '@/types/analytics'

interface Props {
  range: Range
  summary?: Summary
  series?: SeriesPoint[]
  topPosts?: TopPost[]
}

const plural = (n: number, one: string, many = `${one}s`) => `${n.toLocaleString()} ${n === 1 ? one : many}`

function TopPosts({ posts }: { posts: TopPost[] }) {
  const [showAll, setShowAll] = useState(false)
  const max = Math.max(1, ...posts.map(p => p.views))
  const active = posts.filter(p => p.views > 0)
  const hidden = active.length > 0 && active.length < posts.length
  const rows = hidden && !showAll ? active : posts
  return (
    <section aria-labelledby="top-heading" className="rounded-2xl border bg-card p-4 sm:p-6">
      <h2 id="top-heading" className="mb-2 font-serif text-xl font-semibold">Top posts</h2>
      {posts.length === 0 ? (
        <p className="py-8 text-center text-sm text-muted-foreground">No views in this period.</p>
      ) : (
        <>
          <table className="w-full table-fixed text-sm">
            <thead className="text-left text-xs text-muted-foreground">
              <tr className="border-b">
                <th scope="col" className="py-2 pr-3 font-medium">Post</th>
                <th scope="col" className="w-20 px-3 py-2 text-right font-medium">Views</th>
                <th scope="col" className="hidden w-20 px-3 py-2 text-right font-medium sm:table-cell">Likes</th>
                <th scope="col" className="hidden w-24 py-2 pl-3 text-right font-medium sm:table-cell">Comments</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((p, i) => (
                <tr key={p.id} className="group/row border-b last:border-0">
                  <td className="max-w-0 py-3 pr-3">
                    <div className="flex items-center gap-3">
                      <span className="w-4 shrink-0 text-xs text-muted-foreground tabular-nums">{i + 1}</span>
                      <span className="min-w-0 flex-1">
                        <Link href={`/posts/${p.id}/analytics`} className="block truncate py-1 font-serif text-[17px] hover:underline pointer-coarse:py-2.5">{p.title}</Link>
                        <span className="mt-1.5 block h-1 overflow-hidden rounded-full bg-muted" aria-hidden>
                          <span className="block h-full origin-left animate-rise rounded-full bg-brand/70" style={{ width: `${(p.views / max) * 100}%` }} />
                        </span>
                        <span className="mt-1.5 flex items-center gap-2 text-xs text-muted-foreground">
                          <span className="min-w-0 text-pretty sm:hidden">
                            {plural(p.views, 'view')} · {plural(p.likes, 'like')} · {plural(p.comments, 'response')}
                          </span>
                          <Link href={`/posts/${p.id}`} className="-my-2 inline-flex min-h-11 shrink-0 items-center px-2 underline-offset-2 hover:text-foreground hover:underline">Read</Link>
                        </span>
                      </span>
                    </div>
                  </td>
                  <td className="px-3 py-3 text-right font-medium tabular-nums">{p.views.toLocaleString()}</td>
                  <td className="hidden px-3 py-3 text-right tabular-nums text-muted-foreground sm:table-cell">{p.likes.toLocaleString()}</td>
                  <td className="hidden py-3 pl-3 text-right tabular-nums text-muted-foreground sm:table-cell">{p.comments.toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {hidden && (
            <Button variant="ghost" size="sm" className="mt-2 min-h-11 rounded-full text-muted-foreground" aria-expanded={showAll} onClick={() => setShowAll(v => !v)}>
              {showAll ? 'Show only posts with views' : `Show all ${posts.length} posts`}
            </Button>
          )}
        </>
      )}
    </section>
  )
}

export default function AnalyticsIndex({ range, summary, series, topPosts }: Props) {
  const empty = summary?.posts === 0
  return (
    <>
      <div className="flex flex-col gap-8">
        <PageHeader
          title="Analytics"
          description="Views, likes and comments on your posts."
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
                <div className="flex flex-col gap-10">
                  <Skeleton className="h-72 rounded-lg" />
                  <Skeleton className="h-64 rounded-lg" />
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
    </>
  )
}

AnalyticsIndex.layout = { title: 'Analytics' }
