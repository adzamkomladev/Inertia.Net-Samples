import { Link, useForm } from '@inertiajs/react'
import { Check, Loader2 } from 'lucide-react'
import type { ChangeEvent, FormEvent } from 'react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import AuthLayout from '@/layouts/AuthLayout'
import PasswordInput, { StrengthMeter } from '@/components/PasswordInput'

type Field = 'name' | 'email' | 'password' | 'passwordConfirmation'

const fields: { name: Field; label: string; type: string; autoComplete: string }[] = [
  { name: 'name', label: 'Name', type: 'text', autoComplete: 'name' },
  { name: 'email', label: 'Email', type: 'email', autoComplete: 'email' },
  { name: 'password', label: 'Password', type: 'password', autoComplete: 'new-password' },
  { name: 'passwordConfirmation', label: 'Confirm password', type: 'password', autoComplete: 'new-password' },
]

export default function Register() {
  const form = useForm({ name: '', email: '', password: '', passwordConfirmation: '' }).withPrecognition('post', '/register')

  const submit = (e: FormEvent) => {
    e.preventDefault()
    form.submit({ onFinish: () => form.reset('password', 'passwordConfirmation') })
  }

  const input = (f: (typeof fields)[number]) => {
    const props = {
      id: f.name, autoComplete: f.autoComplete, autoFocus: f.name === 'name', required: true, value: form.data[f.name],
      onChange: (e: ChangeEvent<HTMLInputElement>) => form.setData(f.name, e.target.value),
      onBlur: () => form.validate(f.name),
      'aria-invalid': !!form.errors[f.name],
      'aria-describedby': form.errors[f.name] ? `${f.name}-error` : undefined,
    }
    return f.type === 'password' ? <PasswordInput {...props} /> : <Input type={f.type} {...props} />
  }

  return (
    <AuthLayout title="Sign up">
      <h1 className="font-serif text-3xl font-semibold tracking-[-0.02em]">Create your account</h1>
      <p className="mt-1 text-sm text-muted-foreground">Free forever. We will email you a link to verify your address.</p>
      <form onSubmit={submit} noValidate className="mt-8 grid gap-4">
        {fields.map((f) => (
          <div key={f.name} className="grid gap-2">
            <Label htmlFor={f.name}>{f.label}</Label>
            {input(f)}
            {f.name === 'password' && <StrengthMeter password={form.data.password} />}
            {f.name === 'passwordConfirmation' && form.data.passwordConfirmation && form.data.passwordConfirmation === form.data.password && !form.errors.passwordConfirmation && (
              <p className="flex animate-rise items-center gap-1.5 text-xs text-repost"><Check className="size-3.5" aria-hidden /> Passwords match</p>
            )}
            {form.errors[f.name] && (
              <p id={`${f.name}-error`} role="alert" className="text-sm text-destructive">
                {form.errors[f.name]}
              </p>
            )}
          </div>
        ))}
        <Button type="submit" className="mt-2 h-11 w-full rounded-full active:scale-[0.98]" disabled={form.processing}>
          {form.processing && <Loader2 className="animate-spin" aria-hidden />}
          {form.processing ? 'Creating account…' : 'Sign up'}
        </Button>
      </form>
      <p className="mt-6 text-center text-sm text-muted-foreground">
        Already have an account?{' '}
        <Link href="/login" className="font-medium text-brand underline-offset-4 hover:underline">
          Log in
        </Link>
      </p>
    </AuthLayout>
  )
}
