import { Deferred } from '@inertiajs/react'
import type { ReactNode } from 'react'
import { cn } from 'cn'
import { Skeleton } from '@/components/ui/skeleton'
import type { Summary } from '@/types/analytics'

function Delta({ value, prev }: { value: number; prev: number }) {
  if (value === prev) return <span className="text-xs text-muted-foreground">No change</span>
  const up = value > prev
  const text = prev === 0 ? 'New' : `${up ? '+' : '-'}${Math.abs(Math.round(((value - prev) / prev) * 100))}%`
  return (
    <span className={cn('text-xs font-medium tabular-nums', up ? 'text-foreground' : 'text-muted-foreground')}>
      {text}
      <span className="sr-only"> versus previous period</span>
    </span>
  )
}

function Kpi({ title, value, prev, index }: { title: string; value: number; prev?: number; index: number }) {
  return (
    <div className="animate-rise space-y-1 py-4 pr-4" style={{ animationDelay: `${index * 40}ms` }}>
      <p className="text-sm text-muted-foreground">{title}</p>
      <div className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
        <span className="font-serif text-3xl font-semibold tabular-nums tracking-tight">{value.toLocaleString()}</span>
        {prev !== undefined && <Delta value={value} prev={prev} />}
      </div>
    </div>
  )
}

function Shell({ cols, children }: { cols: string; children: ReactNode }) {
  return <div className={cn('grid border-y', cols)}>{children}</div>
}

export default function KpiCards({ summary, showPosts = true }: { summary?: Summary; showPosts?: boolean }) {
  const cols = showPosts ? 'grid-cols-2 sm:grid-cols-4' : 'grid-cols-3'
  return (
    <Deferred
      data="summary"
      fallback={
        <Shell cols={cols}>
          {Array.from({ length: showPosts ? 4 : 3 }, (_, i) => (
            <div key={i} className="space-y-2.5 py-4">
              <Skeleton className="h-4 w-16" />
              <Skeleton className="h-8 w-24" />
            </div>
          ))}
        </Shell>
      }
    >
      {summary && (
        <Shell cols={cols}>
          <Kpi index={0} title="Views" value={summary.views} prev={summary.prevViews} />
          <Kpi index={1} title="Likes" value={summary.likes} prev={summary.prevLikes} />
          <Kpi index={2} title="Comments" value={summary.comments} prev={summary.prevComments} />
          {showPosts && <Kpi index={3} title="Posts" value={summary.posts} />}
        </Shell>
      )}
    </Deferred>
  )
}
