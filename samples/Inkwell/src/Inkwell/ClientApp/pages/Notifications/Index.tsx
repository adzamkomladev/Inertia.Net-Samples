import { useForm, usePage } from '@inertiajs/react'
import { Bell, CheckCheck, Loader2 } from 'lucide-react'
import { useState } from 'react'
import { cn } from 'cn'
import { EmptyState } from '@/components/EmptyState'
import NotificationItem from '@/components/NotificationItem'
import { PageHeader } from '@/components/PageHeader'
import { parseUtc } from '@/components/posts/time'
import { Button } from '@/components/ui/button'
import AppLayout from '@/layouts/AppLayout'
import type { InboxNotice, SharedProps } from '@/types/shared'

function dayLabel(iso: string) {
  const day = parseUtc(iso)
  const today = new Date()
  const start = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime()
  const diff = Math.round((start(today) - start(day)) / 86_400_000)
  if (diff === 0) return 'Today'
  if (diff === 1) return 'Yesterday'
  if (diff < 7) return 'This week'
  return 'Earlier'
}

function groupByDay(items: InboxNotice[]) {
  const groups: { label: string; items: InboxNotice[] }[] = []
  for (const item of items) {
    const label = dayLabel(item.at)
    const last = groups.at(-1)
    if (last?.label === label) last.items.push(item)
    else groups.push({ label, items: [item] })
  }
  return groups
}

export default function Notifications({ items }: { items: InboxNotice[] }) {
  const form = useForm({})
  const { notifications } = usePage<SharedProps>().props
  const [filter, setFilter] = useState<'all' | 'unread'>('all')
  const unread = notifications?.unreadCount ?? 0
  const visible = filter === 'unread' ? items.filter(i => !i.readAt) : items

  return (
    <AppLayout title="Notifications">
      <PageHeader
        title="Notifications"
        description="Likes and comments on your writing, latest 100."
        actions={(
          <Button
            variant="outline" className="rounded-full" disabled={!unread || form.processing}
            onClick={() => form.post('/notifications/read-all', { preserveScroll: true })}
          >
            {form.processing ? <Loader2 className="animate-spin" aria-hidden /> : <CheckCheck aria-hidden />} Mark all read
          </Button>
        )}
      />

      <div role="group" aria-label="Filter notifications" className="mt-6 inline-flex rounded-full bg-muted p-1">
        {(['all', 'unread'] as const).map(value => (
          <button
            key={value} type="button" aria-pressed={filter === value} onClick={() => setFilter(value)}
            className={cn(
              'h-9 rounded-full px-4 text-sm font-medium capitalize transition-[background-color,color,box-shadow]',
              filter === value ? 'bg-background text-foreground shadow-sm' : 'text-muted-foreground hover:text-foreground',
            )}
          >
            {value}{value === 'unread' && unread > 0 && <span className="ml-1.5 rounded-full bg-brand px-1.5 text-[11px] text-brand-foreground tabular-nums">{unread}</span>}
          </button>
        ))}
      </div>

      {visible.length ? (
        <div className="mt-6 space-y-8">
          {groupByDay(visible).map(group => (
            <section key={group.label} aria-label={group.label}>
              <h2 className="mb-1 text-xs font-medium tracking-wide text-muted-foreground uppercase">{group.label}</h2>
              <ul className="space-y-0.5">
                {group.items.map((item, i) => (
                  <li key={item.id} className="animate-rise" style={{ animationDelay: `${Math.min(i, 8) * 30}ms` }}><NotificationItem item={item} /></li>
                ))}
              </ul>
            </section>
          ))}
        </div>
      ) : (
        <EmptyState
          icon={Bell}
          title={filter === 'unread' && items.length ? 'Nothing unread' : 'No notifications yet'}
          description={filter === 'unread' && items.length ? 'You have read everything. Nice.' : 'Likes and comments on your posts will be kept here.'}
          action={filter === 'unread' && items.length ? <Button variant="outline" size="sm" className="rounded-full" onClick={() => setFilter('all')}>Show all</Button> : undefined}
        />
      )}
    </AppLayout>
  )
}
