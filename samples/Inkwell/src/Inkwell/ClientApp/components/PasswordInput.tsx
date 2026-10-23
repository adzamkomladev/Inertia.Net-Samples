import { Eye, EyeOff } from 'lucide-react'
import { useState, type ComponentProps } from 'react'
import { cn } from 'cn'
import { Input } from '@/components/ui/input'

/** Password field with a show/hide toggle that keeps focus in the input. */
export default function PasswordInput({ className, ...props }: Omit<ComponentProps<typeof Input>, 'type'>) {
  const [visible, setVisible] = useState(false)
  return (
    <div className="relative">
      <Input {...props} type={visible ? 'text' : 'password'} className={cn('pr-11', className)} />
      <button
        type="button" aria-label={visible ? 'Hide password' : 'Show password'} aria-pressed={visible}
        onMouseDown={e => e.preventDefault()} onClick={() => setVisible(v => !v)}
        className="absolute top-1/2 right-1 flex size-9 -translate-y-1/2 items-center justify-center rounded-md text-muted-foreground transition-colors hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring"
      >
        {visible ? <EyeOff className="size-4" aria-hidden /> : <Eye className="size-4" aria-hidden />}
      </button>
    </div>
  )
}

/** A rough 0–4 strength score for feedback only; the server enforces the real rules. */
export function passwordScore(password: string) {
  if (!password) return 0
  let score = password.length >= 8 ? 1 : 0
  if (password.length >= 12) score++
  if (/[a-z]/.test(password) && /[A-Z]/.test(password)) score++
  if (/\d/.test(password) && /[^A-Za-z0-9]/.test(password)) score++
  return score
}

export function StrengthMeter({ password }: { password: string }) {
  const score = passwordScore(password)
  const labels = ['Too short', 'Weak', 'Fair', 'Good', 'Strong']
  const tones = ['bg-like', 'bg-like', 'bg-bookmark', 'bg-repost', 'bg-repost']
  if (!password) return null
  return (
    <div className="flex animate-rise items-center gap-3" aria-live="polite">
      <div className="grid flex-1 grid-cols-4 gap-1" aria-hidden>
        {[1, 2, 3, 4].map(i => (
          <span key={i} className={cn('h-1 rounded-full transition-colors duration-300', i <= score ? tones[score] : 'bg-muted')} />
        ))}
      </div>
      <span className="w-16 text-right text-xs text-muted-foreground">{labels[score]}</span>
    </div>
  )
}
