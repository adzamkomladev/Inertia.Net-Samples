import type { LucideIcon } from 'lucide-react'
import type { ReactNode } from 'react'
import { cn } from 'cn'

/** Quiet empty state used by feeds, people, search results and comments. */
export function EmptyState({ icon: Icon, title, description, action, className }: {
  icon: LucideIcon
  title: string
  description?: string
  action?: ReactNode
  className?: string
}) {
  return (
    <div className={cn('flex animate-rise flex-col items-center gap-4 px-6 py-16 text-center', className)}>
      <Icon className="size-6 text-muted-foreground" strokeWidth={1.5} aria-hidden />
      <div className="max-w-sm space-y-1.5">
        <p className="font-serif text-xl font-semibold">{title}</p>
        {description && <p className="text-sm leading-6 text-muted-foreground">{description}</p>}
      </div>
      {action}
    </div>
  )
}
