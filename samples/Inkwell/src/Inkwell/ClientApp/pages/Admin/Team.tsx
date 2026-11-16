import { useState, type FormEvent } from 'react'
import { Loader2, Plus, UserMinus } from 'lucide-react'
import { useForm, usePage } from '@inertiajs/react'
import { toast } from 'sonner'
import { PageHeader } from '@/components/PageHeader'
import PasswordInput from '@/components/PasswordInput'
import { initials } from '@/layouts/AppLayout'
import { AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from '@/components/ui/alert-dialog'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Button, buttonVariants } from '@/components/ui/button'
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
  const promote = useForm({ email: '' })
  const remove = useForm({})
  const [adding, setAdding] = useState(false)
  const [mode, setMode] = useState<'writer' | 'new'>('writer')
  const [removing, setRemoving] = useState<Member | null>(null)

  const close = () => { setAdding(false); form.reset(); form.clearErrors(); promote.reset(); promote.clearErrors(); setMode('writer') }
  const add = (event: FormEvent) => {
    event.preventDefault()
    if (mode === 'writer') promote.post('/admin/team/promote', { preserveScroll: true, onSuccess: close })
    else form.post('/admin/team', { preserveScroll: true, onSuccess: close })
  }
  const busy = mode === 'writer' ? promote.processing : form.processing

  return (
    <>
      <AlertDialog open={removing !== null} onOpenChange={open => { if (!open) setRemoving(null) }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Remove {removing?.name}'s admin access?</AlertDialogTitle>
            <AlertDialogDescription>They keep their writer account, posts and followers. You can make them an administrator again from "Add administrator".</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              disabled={remove.processing}
              className={buttonVariants({ variant: 'destructive' })}
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
          description="Administrators manage who else is an administrator. Access can be given to any verified writer and taken back without touching their posts."
          actions={(
            <Sheet open={adding} onOpenChange={open => (open ? setAdding(true) : close())}>
              <SheetTrigger asChild><Button className="rounded-full active:scale-[0.98]"><Plus /> Add administrator</Button></SheetTrigger>
              <SheetContent side="right" className="flex w-full flex-col sm:max-w-md">
                <SheetHeader>
                  <SheetTitle className="font-serif text-2xl">Add administrator</SheetTitle>
                  <SheetDescription>
                    {mode === 'writer'
                      ? 'Give an existing writer admin access. Their account, posts and followers stay as they are.'
                      : 'Create a new, verified account with admin access. They should change this password after signing in.'}
                  </SheetDescription>
                </SheetHeader>
                <div role="radiogroup" aria-label="Who to add" className="mx-4 grid grid-cols-2 rounded-full bg-muted p-1 text-sm">
                  {([['writer', 'Existing writer'], ['new', 'New account']] as const).map(([value, label]) => (
                    <button
                      key={value} type="button" role="radio" aria-checked={mode === value} onClick={() => setMode(value)}
                      className="min-h-10 rounded-full px-3 font-medium text-muted-foreground transition-colors aria-checked:bg-background aria-checked:text-foreground aria-checked:shadow-xs"
                    >
                      {label}
                    </button>
                  ))}
                </div>
                <form className="flex flex-1 flex-col gap-5 overflow-y-auto px-4 py-2" onSubmit={add} noValidate>
                  {mode === 'writer' ? (
                    <div className="space-y-2">
                      <Label htmlFor="promote-email">Writer's email</Label>
                      <Input id="promote-email" type="email" autoComplete="off" value={promote.data.email} onChange={e => promote.setData('email', e.target.value)} required maxLength={254} aria-invalid={!!promote.errors.email} aria-describedby="promote-email-hint promote-email-error" />
                      <p id="promote-email-hint" className="text-xs text-muted-foreground">The address they sign in with. Their email must be verified.</p>
                      <FieldError id="promote-email-error" message={promote.errors.email} />
                    </div>
                  ) : (<>
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
                  </>)}
                  <SheetFooter className="mt-auto flex-row gap-2 px-0 pb-[env(safe-area-inset-bottom)]">
                    <Button type="button" variant="ghost" className="h-11 flex-1 rounded-full" onClick={close}>Cancel</Button>
                    <Button type="submit" className="h-11 flex-1 rounded-full" disabled={busy}>
                      {busy && <Loader2 className="animate-spin" aria-hidden />}
                      {mode === 'writer' ? (busy ? 'Adding…' : 'Make admin') : (busy ? 'Creating…' : 'Create admin')}
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
                    <p className="flex min-w-0 items-center gap-2 font-medium">
                      <span className="truncate">{member.name}</span>
                      {isSelf && <span className="shrink-0 rounded-full bg-muted px-2 py-0.5 text-[11px] font-medium text-muted-foreground">You</span>}
                    </p>
                    <p className="truncate text-sm text-muted-foreground">{member.email}</p>
                  </div>
                  {!isSelf && (
                    <Button variant="ghost" size="sm" className="rounded-full pointer-coarse:h-11 text-muted-foreground hover:bg-destructive/10 hover:text-destructive" disabled={remove.processing} onClick={() => setRemoving(member)}>
                      <UserMinus aria-hidden /> Remove
                    </Button>
                  )}
                </li>
              )
            })}
          </ul>
        </section>
      </div>
    </>
  )
}

Team.layout = { title: 'Team management' }
