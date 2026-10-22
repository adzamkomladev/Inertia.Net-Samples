import { Link, useForm, usePage } from '@inertiajs/react'
import { Bell } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Sheet, SheetTrigger, SheetContent, SheetHeader, SheetTitle, SheetDescription } from '@/components/ui/sheet'
import type { SharedProps } from '@/types/shared'
import { useState } from 'react'

export default function NotificationBell() {
  const { notifications } = usePage<SharedProps>().props
  const [open, setOpen] = useState(false)
  const form = useForm({})
  return <Sheet open={open} onOpenChange={setOpen}>
    <SheetTrigger asChild><Button variant="ghost" size="icon" className="relative size-11" aria-label={`Notifications${notifications?.unreadCount ? `, ${notifications.unreadCount} unread` : ''}`}><Bell />{Boolean(notifications?.unreadCount) && <span className="absolute top-0 right-0 rounded-full bg-destructive px-1 text-[10px] font-bold text-white">{notifications!.unreadCount > 99 ? '99+' : notifications!.unreadCount}</span>}</Button></SheetTrigger>
    <SheetContent className="w-full sm:max-w-md">
      <SheetHeader><SheetTitle>Notifications</SheetTitle><SheetDescription>Activity on your writing, kept here for you.</SheetDescription></SheetHeader>
      <div className="min-h-0 overflow-y-auto px-4 pb-6">
        <div className="mb-4 flex items-center justify-between gap-2"><p className="text-sm text-muted-foreground" role="status">{notifications?.unreadCount ?? 0} unread</p><Button variant="outline" size="sm" disabled={!notifications?.unreadCount || form.processing} onClick={() => form.post('/notifications/read-all', { preserveScroll: true })}>Mark all read</Button></div>
        <ul className="divide-y">{notifications?.items.length ? notifications.items.map(item => <li key={item.id} className="py-4"><Link href={item.href} onClick={() => setOpen(false)} className="flex gap-3"><span aria-label={item.readAt ? 'Read' : 'Unread'} className={`mt-2 size-2 shrink-0 rounded-full ${item.readAt ? 'bg-muted' : 'bg-primary'}`} /><div><p className="font-medium hover:underline">{item.title}</p><time className="mt-2 block text-xs text-muted-foreground">{new Date(item.at).toLocaleString('en-US', { timeZone: 'UTC' })} UTC</time></div></Link></li>) : <li className="py-12 text-center"><Bell className="mx-auto mb-3 size-8 text-muted-foreground" /><p className="font-medium">You're all caught up</p><p className="mt-2 text-sm text-muted-foreground">New likes and comments will appear here.</p></li>}</ul>
        <Button variant="outline" asChild className="mt-6 w-full"><Link href="/notifications" onClick={() => setOpen(false)}>View all notifications</Link></Button>
      </div>
    </SheetContent>
  </Sheet>
}
