import { Link } from '@inertiajs/react'
import { Bell, Heart, MessageCircle } from 'lucide-react'
import { cn } from 'cn'
import { fullDate, parseUtc, timeAgo } from '@/components/posts/time'
import { UserAvatar } from '@/layouts/AppLayout'
import type { InboxActor, InboxNotice } from '@/types/shared'

/** Consecutive notices of one kind on one essay, shown as a single row ("Ann and 2 others liked …"). */
export interface NoticeGroup {
  key: string
  kind: InboxNotice['kind']
  href: string
  at: string
  unread: boolean
  postTitle: string | null
  title: string
  actors: InboxActor[]
  count: number
}

export function groupNotices(items: InboxNotice[]): NoticeGroup[] {
  const groups: NoticeGroup[] = []
  for (const item of items) {
    const last = groups.at(-1)
    if (last && item.kind !== 'notice' && last.kind === item.kind && last.href === item.href) {
      last.count++
      last.unread ||= !item.readAt
      if (item.actor && !last.actors.some(a => a.id === item.actor!.id)) last.actors.push(item.actor)
      continue
    }
    groups.push({
      key: item.id, kind: item.kind, href: item.href, at: item.at, unread: !item.readAt,
      postTitle: item.postTitle, title: item.title, actors: item.actor ? [item.actor] : [], count: 1,
    })
  }
  return groups
}

const icons = { like: Heart, comment: MessageCircle, notice: Bell }

function Who({ actors, count }: { actors: InboxActor[]; count: number }) {
  if (!actors.length) return <>Someone</>
  const others = Math.max(count, actors.length) - 1
  return (
    <>
      <span className="font-semibold text-foreground">{actors[0]!.name}</span>
      {others > 0 && <> and {others === 1 && actors[1] ? <span className="font-semibold text-foreground">{actors[1].name}</span> : `${others} others`}</>}
    </>
  )
}

/** One inbox row, shared by the bell sheet and the notifications page. */
export default function NotificationItem({ group, onOpen }: { group: NoticeGroup; onOpen?: () => void }) {
  const Icon = icons[group.kind]
  const verb = group.kind === 'like' ? 'liked' : group.kind === 'comment' ? 'responded to' : null
  const lead = group.actors[0]
  return (
    <Link
      href={group.href} onClick={onOpen}
      className="group/notice relative -mx-2 flex items-start gap-3 rounded-xl px-2 py-3 transition-colors hover:bg-accent/60"
    >
      <span className="relative shrink-0">
        {lead ? <UserAvatar user={lead} className="size-9" /> : (
          <span className="flex size-9 items-center justify-center rounded-full bg-muted text-muted-foreground"><Icon className="size-4" aria-hidden /></span>
        )}
        {lead && (
          // The kind rides on the avatar as a small ink badge; a like on an unread notice takes the one accent.
          <span className={cn('absolute -right-1 -bottom-1 flex size-5 items-center justify-center rounded-full bg-background ring-2 ring-background', group.unread && group.kind === 'like' ? 'text-brand' : 'text-muted-foreground')}>
            <Icon className={cn('size-3', group.kind === 'like' && 'fill-current')} aria-hidden />
          </span>
        )}
      </span>
      <span className="min-w-0 flex-1">
        <span className={cn('block text-[15px] leading-snug text-pretty', group.unread ? 'text-foreground' : 'text-muted-foreground')}>
          {verb && group.postTitle ? (
            <><Who actors={group.actors} count={group.count} /> {verb} <span className="font-serif italic">{group.postTitle}</span></>
          ) : group.title}
        </span>
        <time dateTime={group.at} title={`${fullDate(group.at)} ${parseUtc(group.at).toLocaleTimeString('en', { timeStyle: 'short' })}`} className="mt-1 block text-xs text-muted-foreground">
          {timeAgo(group.at)}
        </time>
      </span>
      {group.unread && (
        <span className="mt-2 size-2 shrink-0 rounded-full bg-brand" role="img" aria-label="Unread" />
      )}
    </Link>
  )
}
