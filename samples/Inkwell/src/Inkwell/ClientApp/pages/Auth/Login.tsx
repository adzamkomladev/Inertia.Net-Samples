import { Link, useForm } from '@inertiajs/react'
import { Loader2 } from 'lucide-react'
import type { FormEvent } from 'react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import AuthLayout from '@/layouts/AuthLayout'

export default function Login() {
  const form = useForm({ email: '', password: '', remember: false })
  const returnUrl = typeof window === 'undefined' ? null : new URLSearchParams(window.location.search).get('returnUrl')

  const submit = (e: FormEvent) => {
    e.preventDefault()
    form.post(returnUrl ? `/login?returnUrl=${encodeURIComponent(returnUrl)}` : '/login', {
      onFinish: () => form.reset('password'),
    })
  }

  return (
    <AuthLayout title="Log in">
      <h1 className="text-2xl font-semibold tracking-tight">Welcome back</h1>
      <p className="mt-1 text-sm text-muted-foreground">Log in to write, comment and like.</p>
      <form onSubmit={submit} noValidate className="mt-8 grid gap-4">
        <div className="grid gap-2">
          <Label htmlFor="email">E-mail</Label>
          <Input
            id="email"
            type="email"
            autoComplete="email"
            autoFocus
            required
            value={form.data.email}
            onChange={(e) => form.setData('email', e.target.value)}
            aria-invalid={!!form.errors.email}
            aria-describedby={form.errors.email ? 'email-error' : undefined}
          />
          {form.errors.email && (
            <p id="email-error" role="alert" className="text-sm text-destructive">
              {form.errors.email}
            </p>
          )}
        </div>
        <div className="grid gap-2">
          <Label htmlFor="password">Password</Label>
          <Input
            id="password"
            type="password"
            autoComplete="current-password"
            required
            value={form.data.password}
            onChange={(e) => form.setData('password', e.target.value)}
            aria-invalid={!!form.errors.password}
            aria-describedby={form.errors.password ? 'password-error' : undefined}
          />
          {form.errors.password && (
            <p id="password-error" role="alert" className="text-sm text-destructive">
              {form.errors.password}
            </p>
          )}
        </div>
        <div className="flex items-center gap-2">
          <Switch id="remember" checked={form.data.remember} onCheckedChange={(v) => form.setData('remember', v)} />
          <Label htmlFor="remember" className="font-normal">Remember me</Label>
        </div>
        <Button type="submit" className="h-10 w-full rounded-lg active:scale-[0.98]" disabled={form.processing}>
          {form.processing && <Loader2 className="animate-spin" aria-hidden />}
          {form.processing ? 'Logging in…' : 'Log in'}
        </Button>
      </form>
      <p className="mt-6 text-center text-sm text-muted-foreground">
        New here?{' '}
        <Link href="/register" className="font-medium text-brand underline-offset-4 hover:underline">
          Create an account
        </Link>
      </p>
    </AuthLayout>
  )
}
