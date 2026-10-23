import { Link } from '@inertiajs/react'
import { Bell, Heart, MessageCircle } from 'lucide-react'
import { cn } from 'cn'
import { fullDate, parseUtc, timeAgo } from '@/components/posts/time'
import type { InboxNotice } from '@/types/shared'

/** Picks an icon and tone from the notice text; unknown kinds fall back to a bell. */
function kindOf(title: string) {
  if (/liked/i.test(title)) return { icon: Heart, tone: 'bg-like/12 text-like' }
  if (/comment/i.test(title)) return { icon: MessageCircle, tone: 'bg-comment/12 text-comment' }
  return { icon: Bell, tone: 'bg-muted text-muted-foreground' }
}

/** One inbox row, shared by the bell sheet and the notifications page. */
export default function NotificationItem({ item, onOpen }: { item: InboxNotice; onOpen?: () => void }) {
  const { icon: Icon, tone } = kindOf(item.title)
  const unread = !item.readAt
  return (
    <Link
      href={item.href} onClick={onOpen}
      className="group/notice relative -mx-2 flex items-start gap-3 rounded-xl px-2 py-3 transition-colors hover:bg-accent/60"
    >
      <span className={cn('flex size-9 shrink-0 items-center justify-center rounded-full transition-transform group-hover/notice:scale-105', tone)}>
        <Icon className={cn('size-4', unread && /liked/i.test(item.title) && 'fill-current')} aria-hidden />
      </span>
      <span className="min-w-0 flex-1">
        <span className={cn('block text-[15px] leading-snug', unread ? 'font-medium text-foreground' : 'text-muted-foreground')}>{item.title}</span>
        <time dateTime={item.at} title={`${fullDate(item.at)} ${parseUtc(item.at).toLocaleTimeString('en', { timeStyle: 'short' })}`} className="mt-1 block text-xs text-muted-foreground">
          {timeAgo(item.at)}
        </time>
      </span>
      {unread && <span className="mt-2 size-2 shrink-0 rounded-full bg-brand" aria-label="Unread" />}
    </Link>
  )
}
