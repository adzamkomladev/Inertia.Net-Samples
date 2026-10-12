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
    <div className={cn('flex animate-rise flex-col items-center gap-4 px-6 py-20 text-center', className)}>
      <span className="flex size-12 items-center justify-center rounded-2xl border bg-card text-muted-foreground shadow-xs">
        <Icon className="size-5" strokeWidth={1.75} aria-hidden />
      </span>
      <div className="max-w-sm space-y-1.5">
        <p className="font-medium">{title}</p>
        {description && <p className="text-sm leading-6 text-muted-foreground">{description}</p>}
      </div>
      {action}
    </div>
  )
}
