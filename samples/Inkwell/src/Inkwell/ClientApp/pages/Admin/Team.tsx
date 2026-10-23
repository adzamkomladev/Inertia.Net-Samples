import { useState } from 'react'
import { Plus, ShieldCheck, Users } from 'lucide-react'
import { useForm, usePage } from '@inertiajs/react'
import AppLayout from '@/layouts/AppLayout'
import { AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from '@/components/ui/alert-dialog'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle, SheetTrigger } from '@/components/ui/sheet'
import type { SharedProps } from '@/types/shared'

type Member = { id: string; name: string; email: string }

export default function Team({ members }: { members: Member[] }) {
  const { auth } = usePage<SharedProps>().props
  const form = useForm({ name: '', email: '', password: '' })
  const remove = useForm({})
  const [adding, setAdding] = useState(false)
  const [removing, setRemoving] = useState<string | null>(null)

  return (
    <AppLayout title="Team management">
      <AlertDialog open={removing !== null} onOpenChange={open => { if (!open) setRemoving(null) }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Remove admin access?</AlertDialogTitle>
            <AlertDialogDescription>The account and its posts will remain available.</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction disabled={remove.processing} onClick={() => {
              if (removing) remove.post(`/admin/team/${removing}/remove`, { onSuccess: () => setRemoving(null) })
            }}>Remove access</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <div className="mx-auto max-w-5xl space-y-7 px-4 py-8">
        <header className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <p className="text-sm font-medium text-brand">Workspace access</p>
            <h1 className="mt-1 font-serif text-3xl font-semibold">Team management</h1>
            <p className="mt-2 max-w-xl text-sm text-muted-foreground">Manage who can administer Inkwell. Writers keep their author accounts and posts if access changes.</p>
          </div>
          <Sheet open={adding} onOpenChange={setAdding}>
            <SheetTrigger asChild><Button className="rounded-full"><Plus /> Add administrator</Button></SheetTrigger>
            <SheetContent side="right" className="flex w-full flex-col sm:max-w-lg">
              <SheetHeader>
                <SheetTitle>Add administrator</SheetTitle>
                <SheetDescription>Create a verified admin account. Share the initial password with them securely.</SheetDescription>
              </SheetHeader>
              <form className="flex flex-1 flex-col gap-5 overflow-y-auto px-4 py-5" onSubmit={event => {
                event.preventDefault()
                form.post('/admin/team', { onSuccess: () => { form.reset(); setAdding(false) } })
              }}>
                <div className="space-y-2"><Label htmlFor="team-name">Name</Label><Input id="team-name" autoComplete="name" value={form.data.name} onChange={event => form.setData('name', event.target.value)} required minLength={2} maxLength={80} /></div>
                <div className="space-y-2"><Label htmlFor="team-email">Email</Label><Input id="team-email" type="email" autoComplete="email" value={form.data.email} onChange={event => form.setData('email', event.target.value)} required maxLength={254} /></div>
                <div className="space-y-2"><Label htmlFor="team-password">Initial password</Label><Input id="team-password" type="password" autoComplete="new-password" value={form.data.password} onChange={event => form.setData('password', event.target.value)} required minLength={12} maxLength={128} /><p className="text-xs text-muted-foreground">Use at least 12 characters and share it through a private channel.</p></div>
                {Object.entries(form.errors).map(([field, error]) => <p key={field} role="alert" className="text-sm text-destructive">{error}</p>)}
                <SheetFooter className="mt-auto px-0">
                  <Button type="submit" className="w-full" disabled={form.processing}>{form.processing ? 'Creating…' : 'Create administrator'}</Button>
                </SheetFooter>
              </form>
            </SheetContent>
          </Sheet>
        </header>

        <section className="rounded-2xl border bg-card p-5 sm:p-6">
          <div className="mb-2 flex items-center gap-2 text-sm text-muted-foreground"><Users className="size-4" /> Team overview</div>
          <p className="font-serif text-4xl font-semibold tabular-nums">{members.length}</p>
          <p className="mt-1 text-sm text-muted-foreground">{members.length === 1 ? 'administrator has' : 'administrators have'} access to team settings and account management.</p>
        </section>

        <section className="overflow-hidden rounded-2xl border bg-card">
          <div className="flex items-center justify-between gap-3 border-b px-5 py-4">
            <div><h2 className="font-semibold">Administrators</h2><p className="mt-0.5 text-sm text-muted-foreground">People with full admin access</p></div>
            <span className="rounded-full bg-muted px-2.5 py-1 text-xs font-medium tabular-nums">{members.length} total</span>
          </div>
          <ul className="divide-y px-5">
            {members.map(member => {
              const isSelf = member.id === auth.user?.id
              return <li key={member.id} className="group flex flex-wrap items-center gap-3 py-4 transition-colors">
                <div aria-hidden className="flex size-11 shrink-0 items-center justify-center rounded-full bg-brand/10 font-serif text-lg font-semibold text-brand ring-1 ring-brand/15">{member.name.trim().charAt(0).toUpperCase()}</div>
                <div className="min-w-0 flex-1"><p className="truncate font-medium">{member.name} {isSelf && <span className="ml-1 rounded-full bg-muted px-2 py-0.5 text-[11px] font-medium text-muted-foreground">You</span>}</p><p className="break-all text-sm text-muted-foreground">{member.email}</p></div>
                <span className="inline-flex items-center gap-1.5 rounded-full bg-emerald-500/10 px-2.5 py-1 text-xs font-medium text-emerald-700 dark:text-emerald-300"><ShieldCheck className="size-3.5" /> Admin</span>
                {!isSelf && <Button variant="outline" size="sm" className="rounded-full" disabled={remove.processing} onClick={() => setRemoving(member.id)}>Remove access</Button>}
              </li>
            })}
          </ul>
        </section>
      </div>
    </AppLayout>
  )
}
