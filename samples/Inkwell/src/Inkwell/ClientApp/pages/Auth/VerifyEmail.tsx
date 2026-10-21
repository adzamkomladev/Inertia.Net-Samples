import { router } from '@inertiajs/react'
import { Check, Loader2, MailCheck } from 'lucide-react'
import { useState } from 'react'
import { Button } from '@/components/ui/button'
import AuthLayout from '@/layouts/AuthLayout'

export default function VerifyEmail({ email }: { email: string; verified: boolean }) {
  const [sending, setSending] = useState(false)
  const [sent, setSent] = useState(false)

  return (
    <AuthLayout title="Verify your e-mail">
      <span className="mb-4 flex size-11 items-center justify-center rounded-xl bg-muted text-muted-foreground">
        <MailCheck className="size-5" strokeWidth={1.75} aria-hidden />
      </span>
      <h1 className="font-serif text-3xl font-semibold tracking-[-0.02em]">Check your inbox</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        We sent a verification link to <span className="font-medium text-foreground">{email}</span>. Open it to start
        posting, commenting and liking.
      </p>
      <p className="mt-4 text-sm text-muted-foreground">Nothing arrived? Check your spam folder or request a new link.</p>
      <Button
        className="mt-8 h-10 w-full rounded-lg active:scale-[0.98]"
        disabled={sending}
        onClick={() =>
          router.post('/email/verification-notification', {}, {
            onStart: () => setSending(true),
            onSuccess: () => setSent(true),
            onFinish: () => setSending(false),
          })
        }
      >
        {sending ? <Loader2 className="animate-spin" aria-hidden /> : sent && <Check aria-hidden />}
        {sending ? 'Sending…' : sent ? 'Sent. Send again' : 'Resend verification e-mail'}
      </Button>
      <p className="mt-6 text-center text-sm text-muted-foreground">
        Wrong account?{' '}
        <button
          type="button"
          onClick={() => router.post('/logout')}
          className="rounded font-medium text-brand underline-offset-4 outline-none hover:underline focus-visible:ring-2 focus-visible:ring-ring"
        >
          Log out
        </button>
      </p>
    </AuthLayout>
  )
}
