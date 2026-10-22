import { useState } from 'react'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import AppLayout from '@/layouts/AppLayout'
import { useForm, usePage } from '@inertiajs/react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import type { SharedProps } from '@/types/shared'
export default function Team({
  members,
}: {
  members: { id: string; name: string; email: string }[]
}) {
  const { auth } = usePage<SharedProps>().props
  const form = useForm({ name: '', email: '', password: '' })
  const remove = useForm({})
  const [removing, setRemoving] = useState<string | null>(null)
  return (
    <AppLayout title="Team management">
      <AlertDialog
        open={removing !== null}
        onOpenChange={(open) => {
          if (!open) setRemoving(null)
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Remove admin access?</AlertDialogTitle>
            <AlertDialogDescription>
              Their author account and posts will be kept.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              disabled={remove.processing}
              onClick={() => {
                if (removing)
                  remove.post(`/admin/team/${removing}/remove`, {
                    onSuccess: () => setRemoving(null),
                  })
              }}
            >
              Remove access
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
      <div className="mx-auto max-w-5xl space-y-6 px-4 py-8">
        <div>
          <h1 className="font-serif text-3xl font-semibold">Team management</h1>
          <p className="mt-2 text-sm text-muted-foreground">
            Manage administrators. Author accounts and publishing remain
            available to verified members.
          </p>
        </div>
        <div className="grid gap-6 lg:grid-cols-[1fr_22rem]">
          <section className="rounded-xl border bg-card p-6">
            <h2 className="mb-4 font-semibold">
              Administrators ({members.length})
            </h2>
            <ul className="divide-y">
              {members.map((member) => (
                <li
                  key={member.id}
                  className="flex flex-wrap items-center gap-3 py-4"
                >
                  <div className="min-w-0 flex-1">
                    <p className="font-medium">{member.name}</p>
                    <p className="break-all text-sm text-muted-foreground">
                      {member.email}
                    </p>
                  </div>
                  {member.id !== auth.user?.id && (
                    <Button
                      variant="outline"
                      size="sm"
                      disabled={remove.processing}
                      onClick={() => setRemoving(member.id)}
                    >
                      Remove access
                    </Button>
                  )}
                </li>
              ))}
            </ul>
          </section>
          <form
            className="space-y-4 rounded-xl border bg-card p-6"
            onSubmit={(event) => {
              event.preventDefault()
              form.post('/admin/team', { onSuccess: () => form.reset() })
            }}
          >
            <h2 className="font-semibold">Add administrator</h2>
            <div className="space-y-2">
              <Label htmlFor="team-name">Name</Label>
              <Input
                id="team-name"
                value={form.data.name}
                onChange={(event) => form.setData('name', event.target.value)}
                required
                minLength={2}
                maxLength={80}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="team-email">Email</Label>
              <Input
                id="team-email"
                type="email"
                value={form.data.email}
                onChange={(event) => form.setData('email', event.target.value)}
                required
                maxLength={254}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="team-password">Initial password</Label>
              <Input
                id="team-password"
                type="password"
                autoComplete="new-password"
                value={form.data.password}
                onChange={(event) =>
                  form.setData('password', event.target.value)
                }
                required
                minLength={12}
                maxLength={128}
              />
            </div>
            {Object.entries(form.errors).map(([field, error]) => (
              <p key={field} role="alert" className="text-sm text-destructive">
                {error}
              </p>
            ))}
            <Button type="submit" disabled={form.processing}>
              Create administrator
            </Button>
          </form>
        </div>
      </div>
    </AppLayout>
  )
}
