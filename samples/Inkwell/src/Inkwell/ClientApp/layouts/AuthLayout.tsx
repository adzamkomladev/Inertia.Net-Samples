import { Link, usePage } from '@inertiajs/react'
import type { ReactNode } from 'react'
import { Toaster } from '@/components/ui/sonner'
import { useDocumentTitle } from '@/hooks/use-document-title'
import { useFlashToasts } from '@/hooks/use-flash-toasts'
import { useTheme } from '@/hooks/use-theme'
import { BrandMark } from '@/layouts/AppLayout'
import type { Flash, SharedProps } from '@/types/shared'

/** Centered shell for the auth pages (login, register, verify e-mail). */
export default function AuthLayout({ children, title }: { children: ReactNode; title?: string }) {
  const page = usePage<SharedProps>()
  const { appName, auth } = page.props

  useTheme(auth.user?.theme ?? 'system')
  useFlashToasts(page.flash as Flash)
  useDocumentTitle(title, appName)

  return (
    <div className="grid min-h-screen bg-background lg:grid-cols-2">
      <div className="flex flex-col p-6 sm:p-8">
        <Link
          href="/posts"
          className="flex w-fit items-center gap-2 rounded-lg text-sm font-semibold tracking-tight outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          <BrandMark />
          {appName}
        </Link>
        <main className="animate-rise mx-auto flex w-full max-w-sm flex-1 flex-col justify-center py-10">{children}</main>
        <p className="text-center text-xs text-muted-foreground">
          &copy; {new Date().getFullYear()} {appName}
        </p>
      </div>
      <aside className="hidden items-center bg-muted p-16 lg:flex">
        <blockquote className="max-w-md font-serif text-3xl leading-snug tracking-tight">
          Write the thing you wish you had been able to read.
        </blockquote>
      </aside>
      <Toaster theme={auth.user?.theme ?? 'system'} closeButton />
    </div>
  )
}
