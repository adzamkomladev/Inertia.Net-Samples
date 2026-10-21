import { Link } from '@inertiajs/react'
import type { ReactNode } from 'react'
import { cn } from 'cn'

/** Title block shared by every app page: title, optional description and right-aligned actions. */
export function PageHeader({ title, description, actions, className }: {
  title: ReactNode
  description?: ReactNode
  actions?: ReactNode
  className?: string
}) {
  return (
    <header className={cn('flex flex-wrap items-end justify-between gap-x-4 gap-y-3', className)}>
      <div className="min-w-0 space-y-1">
        <h1 className="font-serif text-[2rem] leading-tight font-semibold tracking-[-0.02em] text-balance sm:text-4xl">{title}</h1>
        {description && <p className="text-base text-muted-foreground">{description}</p>}
      </div>
      {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
    </header>
  )
}

/** Underlined link tabs (feed modes, profile sections). */
export function LinkTabs({ tabs, current, label, className }: { tabs: { href: string; label: string; value: string }[]; current: string; label: string; className?: string }) {
  return (
    <nav aria-label={label} className={cn('flex gap-6 border-b', className)}>
      {tabs.map(tab => {
        const active = tab.value === current
        return (
          <Link
            key={tab.value} href={tab.href} aria-current={active ? 'page' : undefined} preserveScroll
            className={cn(
              'relative -mb-px flex min-h-11 items-center text-[15px] text-muted-foreground transition-colors hover:text-foreground',
              'after:absolute after:inset-x-0 after:bottom-0 after:h-0.5 after:origin-center after:scale-x-0 after:rounded-full after:bg-foreground after:transition-transform after:duration-300',
              active && 'font-medium text-foreground after:scale-x-100',
            )}
          >
            {tab.label}
          </Link>
        )
      })}
    </nav>
  )
}
