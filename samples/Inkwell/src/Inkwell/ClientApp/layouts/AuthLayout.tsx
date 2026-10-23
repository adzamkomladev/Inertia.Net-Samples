import { Link, usePage } from '@inertiajs/react'
import type { ReactNode } from 'react'
import { Toaster } from '@/components/ui/sonner'
import { useDocumentTitle } from '@/hooks/use-document-title'
import { useFlashToasts } from '@/hooks/use-flash-toasts'
import { useTheme } from '@/hooks/use-theme'
import { BrandMark } from '@/layouts/AppLayout'
import type { Flash, SharedProps } from '@/types/shared'

/** Split shell for the auth pages: an editorial panel on wide screens, the form on the right. */
export default function AuthLayout({ children, title }: { children: ReactNode; title?: string }) {
  const page = usePage<SharedProps>()
  const { appName, auth } = page.props

  useTheme(auth.user?.theme ?? 'system')
  useFlashToasts(page.flash as Flash)
  useDocumentTitle(title, appName)

  return (
    <div className="grid min-h-screen bg-background lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
      <aside className="paper relative hidden flex-col justify-between overflow-hidden border-r p-12 lg:flex">
        <Link href="/posts" className="w-fit rounded-md" aria-label={`${appName} home`}><BrandMark className="text-3xl" /></Link>
        <figure className="max-w-md">
          <blockquote className="font-serif text-[2rem] leading-snug font-medium tracking-[-0.015em] text-balance">
            “Writing is thinking on paper. Reading is borrowing someone else’s thoughts for an evening.”
          </blockquote>
          <figcaption className="mt-5 text-sm text-muted-foreground">The {appName} idea, in one line.</figcaption>
        </figure>
        {/* A small fan of essay cards as decoration. */}
        <div aria-hidden className="relative h-44">
          {[2, 1, 0].map(i => (
            <div
              key={i} className="absolute w-72 rounded-2xl border bg-card p-4 shadow-md"
              style={{ left: i * 36, top: i * 14, transform: `rotate(${i * 4 - 4}deg)` }}
            >
              <div className="h-2 w-20 rounded-full bg-brand/70" />
              <div className="mt-3 h-3 w-48 rounded-full bg-foreground/80" />
              <div className="mt-2.5 h-2 w-56 rounded-full bg-muted-foreground/20" />
              <div className="mt-1.5 h-2 w-40 rounded-full bg-muted-foreground/20" />
              <div className="mt-4 flex gap-2"><div className="h-2 w-8 rounded-full bg-like/40" /><div className="h-2 w-8 rounded-full bg-comment/40" /></div>
            </div>
          ))}
        </div>
      </aside>
      <div className="flex min-h-screen flex-col items-center px-5 py-8 sm:py-12">
        <Link href="/posts" className="rounded-md lg:hidden" aria-label={`${appName} home`}><BrandMark className="text-3xl" /></Link>
        <main className="flex w-full max-w-sm flex-1 animate-rise flex-col justify-center py-10">{children}</main>
        <p className="text-center text-xs text-muted-foreground">&copy; {new Date().getFullYear()} {appName}</p>
      </div>
      <Toaster theme={auth.user?.theme ?? 'system'} closeButton />
    </div>
  )
}
