import { Deferred } from '@inertiajs/react'
import { ArrowDownRight, ArrowUpRight, Eye, FileText, Heart, MessageCircle, Minus, type LucideIcon } from 'lucide-react'
import type { ReactNode } from 'react'
import { cn } from 'cn'
import { Skeleton } from '@/components/ui/skeleton'
import type { Summary } from '@/types/analytics'

function Delta({ value, prev }: { value: number; prev: number }) {
  if (value === prev) {
    return <span className="inline-flex items-center gap-0.5 rounded-full bg-muted px-1.5 py-0.5 text-[11px] text-muted-foreground"><Minus className="size-3" aria-hidden /> No change</span>
  }
  const up = value > prev
  const text = prev === 0 ? 'New' : `${Math.abs(Math.round(((value - prev) / prev) * 100))}%`
  const Icon = up ? ArrowUpRight : ArrowDownRight
  return (
    <span className={cn('inline-flex items-center gap-0.5 rounded-full px-1.5 py-0.5 text-[11px] font-medium tabular-nums', up ? 'bg-repost/12 text-repost' : 'bg-like/12 text-like')}>
      <Icon className="size-3" aria-hidden />{text}
      <span className="sr-only"> {up ? 'up' : 'down'} versus previous period</span>
    </span>
  )
}

function Kpi({ title, value, prev, index, icon: Icon, tone }: { title: string; value: number; prev?: number; index: number; icon: LucideIcon; tone: string }) {
  return (
    <div className="animate-rise rounded-2xl border bg-card p-4 transition-shadow hover:shadow-sm" style={{ animationDelay: `${index * 50}ms` }}>
      <div className="flex items-center justify-between gap-2">
        <p className="text-sm text-muted-foreground">{title}</p>
        <span className={cn('flex size-7 items-center justify-center rounded-full', tone)}><Icon className="size-3.5" aria-hidden /></span>
      </div>
      <p className="mt-2 font-serif text-3xl font-semibold tracking-tight tabular-nums">{value.toLocaleString()}</p>
      <div className="mt-1 h-5">{prev !== undefined && <Delta value={value} prev={prev} />}</div>
    </div>
  )
}

function Shell({ cols, children }: { cols: string; children: ReactNode }) {
  return <div className={cn('grid gap-3', cols)}>{children}</div>
}

export default function KpiCards({ summary, showPosts = true }: { summary?: Summary; showPosts?: boolean }) {
  const cols = showPosts ? 'grid-cols-2 sm:grid-cols-4' : 'grid-cols-1 sm:grid-cols-3'
  return (
    <Deferred
      data="summary"
      fallback={
        <Shell cols={cols}>
          {Array.from({ length: showPosts ? 4 : 3 }, (_, i) => <Skeleton key={i} className="h-[7.5rem] rounded-2xl" />)}
        </Shell>
      }
    >
      {summary && (
        <Shell cols={cols}>
          <Kpi index={0} title="Views" value={summary.views} prev={summary.prevViews} icon={Eye} tone="bg-brand/12 text-brand" />
          <Kpi index={1} title="Likes" value={summary.likes} prev={summary.prevLikes} icon={Heart} tone="bg-like/12 text-like" />
          <Kpi index={2} title="Comments" value={summary.comments} prev={summary.prevComments} icon={MessageCircle} tone="bg-comment/12 text-comment" />
          {showPosts && <Kpi index={3} title="Posts" value={summary.posts} icon={FileText} tone="bg-muted text-muted-foreground" />}
        </Shell>
      )}
    </Deferred>
  )
}
