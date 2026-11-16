import { Deferred, Link } from '@inertiajs/react'
import { ArrowLeft } from 'lucide-react'
import KpiCards from '@/components/analytics/KpiCards'
import RangeTabs from '@/components/analytics/RangeTabs'
import TrafficChart from '@/components/analytics/TrafficChart'
import { PageHeader } from '@/components/PageHeader'
import { Skeleton } from '@/components/ui/skeleton'
import type { Range, SeriesPoint, Summary } from '@/types/analytics'

const plural = (n: number, one: string) => `${n.toLocaleString()} ${n === 1 ? one : `${one}s`}`

interface Props {
  post: { id: string; title: string; createdAt: string; viewCount: number; likeCount: number; commentCount: number }
  range: Range
  summary?: Summary
  series?: SeriesPoint[]
}

export default function PostAnalytics({ post, range, summary, series }: Props) {
  return (
    <>
      <div className="flex flex-col gap-8">
        <Link href={`/posts/${post.id}`} className="inline-flex min-h-11 w-fit items-center gap-1 text-sm text-muted-foreground transition-colors hover:text-foreground">
          <ArrowLeft className="size-4" aria-hidden /> Back to post
        </Link>
        <PageHeader
          title={post.title}
          description={`Lifetime: ${plural(post.viewCount, 'view')}, ${plural(post.likeCount, 'like')}, ${plural(post.commentCount, 'comment')}`}
          actions={<RangeTabs range={range} only={['summary', 'series']} />}
        />
        <KpiCards summary={summary} showPosts={false} />
        <Deferred data="series" fallback={<Skeleton className="h-72 rounded-lg" />}>
          {series && <TrafficChart series={series} />}
        </Deferred>
      </div>
    </>
  )
}

PostAnalytics.layout = (props: Props) => ({ title: `Analytics: ${props.post.title}` })
