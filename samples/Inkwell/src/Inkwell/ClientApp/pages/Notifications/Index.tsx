import { Link, useForm, usePage } from '@inertiajs/react'
import { Bell } from 'lucide-react'
import AppLayout from '@/layouts/AppLayout'
import { Button } from '@/components/ui/button'
import type { InboxNotice, SharedProps } from '@/types/shared'

export default function Notifications({ items }: { items: InboxNotice[] }) {
  const form = useForm({})
  const { notifications } = usePage<SharedProps>().props
  return <AppLayout title="Notifications"><div className="mb-8 flex flex-wrap items-center justify-between gap-4"><div><h1 className="font-serif text-3xl">Notifications</h1><p className="mt-2 text-sm text-muted-foreground">Likes and comments on your writing. Showing your latest 100 updates.</p></div><Button variant="outline" disabled={!notifications?.unreadCount || form.processing} onClick={() => form.post('/notifications/read-all', { preserveScroll: true })}>Mark all read</Button></div><ul className="divide-y">{items.length ? items.map(item => <li key={item.id} className="flex items-start gap-3 py-5"><span aria-label={item.readAt ? 'Read' : 'Unread'} className={`mt-2 size-2 shrink-0 rounded-full ${item.readAt ? 'bg-muted' : 'bg-primary'}`} /><div><Link href={item.href} className="font-medium hover:underline">{item.title}</Link><p className="mt-2 text-xs text-muted-foreground">{new Date(item.at).toLocaleString('en-US', { timeZone: 'UTC' })} UTC</p></div></li>) : <li className="py-16 text-center"><Bell className="mx-auto mb-4 size-8 text-muted-foreground" /><p>No notifications yet</p><p className="mt-2 text-sm text-muted-foreground">We'll keep new activity on your posts here.</p></li>}</ul></AppLayout>
}
