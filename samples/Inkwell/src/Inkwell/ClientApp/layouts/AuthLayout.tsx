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
    <div className="flex min-h-screen flex-col items-center bg-background px-5 py-8 sm:py-12">
      <Link href="/posts" className="rounded-md" aria-label={`${appName} home`}><BrandMark className="text-3xl" /></Link>
      <main className="animate-rise flex w-full max-w-sm flex-1 flex-col justify-center py-10">{children}</main>
      <p className="text-center text-xs text-muted-foreground">&copy; {new Date().getFullYear()} {appName}</p>
      <Toaster theme={auth.user?.theme ?? 'system'} closeButton />
    </div>
  )
}
