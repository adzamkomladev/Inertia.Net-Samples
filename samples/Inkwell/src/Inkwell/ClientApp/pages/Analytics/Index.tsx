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
  return (
    <section aria-labelledby="top-heading">
      <h2 id="top-heading" className="mb-3 font-serif text-xl font-semibold">Top posts</h2>
      {posts.length === 0 ? (
        <p className="border-y py-8 text-center text-sm text-muted-foreground">No views in this period.</p>
      ) : (
        <table className="w-full table-fixed text-sm">
          <thead className="text-left text-xs text-muted-foreground">
            <tr className="border-b">
              <th scope="col" className="py-2 pr-3 font-medium">Post</th>
              <th scope="col" className="w-20 px-3 py-2 text-right font-medium">Views</th>
              <th scope="col" className="hidden w-20 px-3 py-2 text-right font-medium sm:table-cell">Likes</th>
              <th scope="col" className="hidden w-28 py-2 pl-3 text-right font-medium sm:table-cell">Comments</th>
            </tr>
          </thead>
          <tbody>
            {posts.map(p => (
              <tr key={p.id} className="border-b last:border-0">
                <td className="max-w-0 py-3.5 pr-3">
                  <Link href={`/posts/${p.id}/analytics`} className="block truncate font-serif text-[17px] hover:underline">{p.title}</Link>
                </td>
                <td className="px-3 py-3.5 text-right font-medium tabular-nums">{p.views.toLocaleString()}</td>
                <td className="hidden px-3 py-3.5 text-right tabular-nums text-muted-foreground sm:table-cell">{p.likes.toLocaleString()}</td>
                <td className="hidden py-3.5 pl-3 text-right tabular-nums text-muted-foreground sm:table-cell">{p.comments.toLocaleString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}

export default function AnalyticsIndex({ range, summary, series, topPosts }: Props) {
  const empty = summary?.posts === 0
  return (
    <AppLayout title="Analytics">
      <div className="flex flex-col gap-10">
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
              <div className="flex flex-col gap-10">
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
