import { useState, type FormEvent } from 'react'
import { Loader2, Plus, ShieldCheck, UserMinus } from 'lucide-react'
import { useForm, usePage } from '@inertiajs/react'
import { toast } from 'sonner'
import { PageHeader } from '@/components/PageHeader'
import PasswordInput from '@/components/PasswordInput'
import AppLayout, { initials } from '@/layouts/AppLayout'
import { AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from '@/components/ui/alert-dialog'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle, SheetTrigger } from '@/components/ui/sheet'
import type { SharedProps } from '@/types/shared'

type Member = { id: string; name: string; email: string }

function FieldError({ id, message }: { id: string; message?: string }) {
  return message ? <p id={id} role="alert" className="animate-rise text-sm text-destructive">{message}</p> : null
}

export default function Team({ members }: { members: Member[] }) {
  const { auth } = usePage<SharedProps>().props
  const form = useForm({ name: '', email: '', password: '' })
  const remove = useForm({})
  const [adding, setAdding] = useState(false)
  const [removing, setRemoving] = useState<Member | null>(null)

  const add = (event: FormEvent) => {
    event.preventDefault()
    form.post('/admin/team', { preserveScroll: true, onSuccess: () => { form.reset(); setAdding(false) } })
  }

  return (
    <AppLayout title="Team management">
      <AlertDialog open={removing !== null} onOpenChange={open => { if (!open) setRemoving(null) }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Remove {removing?.name}'s admin access?</AlertDialogTitle>
            <AlertDialogDescription>They keep their writer account, posts and followers. You can add them back at any time.</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              disabled={remove.processing}
              className="bg-destructive text-white hover:bg-destructive/90"
              onClick={(e) => {
                e.preventDefault()
                if (removing) remove.post(`/admin/team/${removing.id}/remove`, { preserveScroll: true, onSuccess: () => setRemoving(null), onError: errors => toast.error(errors.role ?? 'Could not remove access') })
              }}
            >
              {remove.processing && <Loader2 className="animate-spin" aria-hidden />} Remove access
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <div className="space-y-8">
        <PageHeader
          title="Team"
          description="Administrators can manage the team. Writers keep their accounts and posts if access changes."
          actions={(
            <Sheet open={adding} onOpenChange={setAdding}>
              <SheetTrigger asChild><Button className="rounded-full active:scale-[0.98]"><Plus /> Add administrator</Button></SheetTrigger>
              <SheetContent side="right" className="flex w-full flex-col sm:max-w-md">
                <SheetHeader>
                  <SheetTitle className="font-serif text-2xl">Add administrator</SheetTitle>
                  <SheetDescription>Creates a verified admin account. Share the initial password through a private channel.</SheetDescription>
                </SheetHeader>
                <form className="flex flex-1 flex-col gap-5 overflow-y-auto px-4 py-2" onSubmit={add} noValidate>
                  <div className="space-y-2">
                    <Label htmlFor="team-name">Name</Label>
                    <Input id="team-name" autoComplete="name" value={form.data.name} onChange={e => form.setData('name', e.target.value)} required minLength={2} maxLength={80} aria-invalid={!!form.errors.name} aria-describedby="team-name-error" />
                    <FieldError id="team-name-error" message={form.errors.name} />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="team-email">Email</Label>
                    <Input id="team-email" type="email" autoComplete="email" value={form.data.email} onChange={e => form.setData('email', e.target.value)} required maxLength={254} aria-invalid={!!form.errors.email} aria-describedby="team-email-error" />
                    <FieldError id="team-email-error" message={form.errors.email} />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="team-password">Initial password</Label>
                    <PasswordInput id="team-password" autoComplete="new-password" value={form.data.password} onChange={e => form.setData('password', e.target.value)} required minLength={12} maxLength={128} aria-invalid={!!form.errors.password} aria-describedby="team-password-hint team-password-error" />
                    <p id="team-password-hint" className="text-xs text-muted-foreground">At least 12 characters.</p>
                    <FieldError id="team-password-error" message={form.errors.password} />
                  </div>
                  <SheetFooter className="mt-auto flex-row gap-2 px-0">
                    <Button type="button" variant="ghost" className="flex-1 rounded-full" onClick={() => setAdding(false)}>Cancel</Button>
                    <Button type="submit" className="flex-1 rounded-full" disabled={form.processing}>
                      {form.processing && <Loader2 className="animate-spin" aria-hidden />}
                      {form.processing ? 'Creating…' : 'Create admin'}
                    </Button>
                  </SheetFooter>
                </form>
              </SheetContent>
            </Sheet>
          )}
        />

        <section className="overflow-hidden rounded-2xl border bg-card" aria-labelledby="admins-heading">
          <div className="flex items-center justify-between gap-3 border-b px-5 py-4">
            <div>
              <h2 id="admins-heading" className="font-semibold">Administrators</h2>
              <p className="mt-0.5 text-sm text-muted-foreground">Full access to team management.</p>
            </div>
            <span className="rounded-full bg-muted px-2.5 py-1 text-xs font-medium tabular-nums">{members.length} {members.length === 1 ? 'member' : 'members'}</span>
          </div>
          <ul className="divide-y">
            {members.map((member, i) => {
              const isSelf = member.id === auth.user?.id
              return (
                <li key={member.id} className="flex animate-rise flex-wrap items-center gap-3 px-5 py-4 transition-colors hover:bg-muted/40" style={{ animationDelay: `${Math.min(i, 8) * 40}ms` }}>
                  <Avatar className="size-11"><AvatarFallback className="bg-brand/10 font-serif text-brand">{initials(member.name)}</AvatarFallback></Avatar>
                  <div className="min-w-0 flex-1">
                    <p className="truncate font-medium">{member.name}{isSelf && <span className="ml-2 rounded-full bg-muted px-2 py-0.5 text-[11px] font-medium text-muted-foreground">You</span>}</p>
                    <p className="truncate text-sm text-muted-foreground">{member.email}</p>
                  </div>
                  <span className="inline-flex items-center gap-1.5 rounded-full bg-repost/10 px-2.5 py-1 text-xs font-medium text-repost"><ShieldCheck className="size-3.5" aria-hidden /> Admin</span>
                  {!isSelf && (
                    <Button variant="ghost" size="sm" className="rounded-full text-muted-foreground hover:bg-destructive/10 hover:text-destructive" disabled={remove.processing} onClick={() => setRemoving(member)}>
                      <UserMinus aria-hidden /> Remove
                    </Button>
                  )}
                </li>
              )
            })}
          </ul>
        </section>
      </div>
    </AppLayout>
  )
}
