import { Deferred, Link } from '@inertiajs/react'
import { ArrowLeft } from 'lucide-react'
import KpiCards from '@/components/analytics/KpiCards'
import RangeTabs from '@/components/analytics/RangeTabs'
import TrafficChart from '@/components/analytics/TrafficChart'
import { PageHeader } from '@/components/PageHeader'
import { Skeleton } from '@/components/ui/skeleton'
import AppLayout from '@/layouts/AppLayout'
import type { Range, SeriesPoint, Summary } from '@/types/analytics'

interface Props {
  post: { id: string; title: string; createdAt: string; viewCount: number; likeCount: number; commentCount: number }
  range: Range
  summary?: Summary
  series?: SeriesPoint[]
}

export default function PostAnalytics({ post, range, summary, series }: Props) {
  return (
    <AppLayout title={`Analytics: ${post.title}`}>
      <div className="flex flex-col gap-6">
        <Link href={`/posts/${post.id}`} className="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground transition-colors hover:text-foreground">
          <ArrowLeft className="size-4" aria-hidden /> Back to post
        </Link>
        <PageHeader
          title={<span className="font-serif">{post.title}</span>}
          description={`Lifetime: ${post.viewCount.toLocaleString()} views, ${post.likeCount.toLocaleString()} likes, ${post.commentCount.toLocaleString()} comments`}
          actions={<RangeTabs range={range} only={['summary', 'series']} />}
        />
        <KpiCards summary={summary} showPosts={false} />
        <Deferred data="series" fallback={<Skeleton className="h-[26rem] rounded-xl" />}>
          {series && <TrafficChart series={series} />}
        </Deferred>
      </div>
    </AppLayout>
  )
}
