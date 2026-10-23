import { Link, useForm, usePage } from '@inertiajs/react'
import { Bell, CheckCheck, Loader2 } from 'lucide-react'
import { useState } from 'react'
import { cn } from 'cn'
import NotificationItem from '@/components/NotificationItem'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle, SheetTrigger } from '@/components/ui/sheet'
import type { SharedProps } from '@/types/shared'

export default function NotificationBell() {
  const { notifications } = usePage<SharedProps>().props
  const [open, setOpen] = useState(false)
  const form = useForm({})
  const unread = notifications?.unreadCount ?? 0
  // Remember the count we last showed so the bell only rings when it goes up.
  const [shown, setShown] = useState(unread)
  const [ringing, setRinging] = useState(false)
  if (unread !== shown) {
    if (unread > shown) setRinging(true)
    setShown(unread)
  }

  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetTrigger asChild>
        <Button variant="ghost" size="icon" className="relative size-11 rounded-full" aria-label={`Notifications${unread ? `, ${unread} unread` : ''}`}>
          <Bell className={cn(ringing && 'animate-ring')} onAnimationEnd={() => setRinging(false)} />
          {unread > 0 && (
            <span key={unread} className="absolute top-1.5 right-1.5 flex h-4 min-w-4 animate-pop items-center justify-center rounded-full bg-brand px-1 text-[10px] leading-none font-semibold text-brand-foreground ring-2 ring-background tabular-nums">
              {unread > 99 ? '99+' : unread}
            </span>
          )}
        </Button>
      </SheetTrigger>
      <SheetContent className="w-full gap-0 sm:max-w-md">
        <SheetHeader className="border-b">
          <SheetTitle className="font-serif text-2xl">Notifications</SheetTitle>
          <SheetDescription>{unread ? `${unread} unread · activity on your writing` : 'You are all caught up.'}</SheetDescription>
        </SheetHeader>
        <div className="min-h-0 flex-1 overflow-y-auto px-4 py-3">
          {notifications?.items.length ? (
            <ul className="space-y-0.5">
              {notifications.items.map(item => <li key={item.id}><NotificationItem item={item} onOpen={() => setOpen(false)} /></li>)}
            </ul>
          ) : (
            <div className="flex flex-col items-center gap-3 py-16 text-center">
              <span className="flex size-12 items-center justify-center rounded-full bg-muted"><Bell className="size-5 text-muted-foreground" aria-hidden /></span>
              <p className="font-serif text-lg font-semibold">You're all caught up</p>
              <p className="max-w-60 text-sm text-muted-foreground">New likes and comments on your posts will show up here.</p>
            </div>
          )}
        </div>
        <div className="flex gap-2 border-t p-4">
          <Button
            variant="outline" className="flex-1 rounded-full" disabled={!unread || form.processing}
            onClick={() => form.post('/notifications/read-all', { preserveScroll: true })}
          >
            {form.processing ? <Loader2 className="animate-spin" aria-hidden /> : <CheckCheck aria-hidden />} Mark all read
          </Button>
          <Button asChild className="flex-1 rounded-full"><Link href="/notifications" onClick={() => setOpen(false)}>View all</Link></Button>
        </div>
      </SheetContent>
    </Sheet>
  )
}
