import NotificationBell from '@/components/NotificationBell'
import NotificationSound from '@/components/NotificationSound'
import { Link, router, usePage } from '@inertiajs/react'
import {
  Archive, BarChart3, Bookmark, FileText, Home, LogIn, LogOut, PenLine, Search, Settings, UserRound, Users, X,
} from 'lucide-react'
import { useState, type FormEvent, type ReactNode } from 'react'
import { cn } from 'cn'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel,
  DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Toaster } from '@/components/ui/sonner'
import { useDocumentTitle } from '@/hooks/use-document-title'
import { useFlashToasts } from '@/hooks/use-flash-toasts'
import { useTheme } from '@/hooks/use-theme'
import { useRealtime } from '@/hooks/use-realtime'
import type { AuthUser, Flash, SharedProps } from '@/types/shared'

export function initials(name: string) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]!.toUpperCase()).join('') || '?'
}

/** Serif wordmark used as the logo. */
export function BrandMark({ className }: { className?: string }) {
  const { appName } = usePage<SharedProps>().props
  return <span className={cn('font-serif text-[1.65rem] leading-none font-semibold tracking-[-0.03em]', className)}>{appName}</span>
}

type NavEntry = { href: string; label: string; icon: typeof Home }

function useActive() {
  const url = usePage().url
  return (href: string) => {
    const path = href.split('?')[0]!
    if (href.includes('?')) return url.split('&')[0] === href
    if (path === '/posts') return url === '/posts' || url.startsWith('/posts?') || url === '/'
    return url === href || url.startsWith(`${path}?`) || url.startsWith(`${path}/`)
  }
}

export function UserAvatar({ user, className }: { user: { name: string; avatarUrl: string | null }; className?: string }) {
  return (
    <Avatar className={cn('size-8', className)}>
      {user.avatarUrl && <AvatarImage src={user.avatarUrl} alt="" />}
      <AvatarFallback className="text-xs">{initials(user.name)}</AvatarFallback>
    </Avatar>
  )
}

function AccountMenu({ user, children }: { user: AuthUser; children: ReactNode }) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>{children}</DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-60">
        <DropdownMenuLabel className="font-normal">
          <div className="truncate text-sm font-medium">{user.name}</div>
          <div className="truncate text-xs text-muted-foreground">{user.email}</div>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem asChild><Link href={`/users/${user.id}`}><UserRound /> Profile</Link></DropdownMenuItem>
        <DropdownMenuItem asChild><Link href="/me/posts"><FileText /> My posts</Link></DropdownMenuItem>
        <DropdownMenuItem asChild><Link href="/bookmarks"><Bookmark /> Bookmarks</Link></DropdownMenuItem>
        <DropdownMenuItem asChild><Link href="/analytics"><BarChart3 /> Analytics</Link></DropdownMenuItem>
        <DropdownMenuItem asChild><Link href="/me/archives"><Archive /> Archives</Link></DropdownMenuItem>
        <DropdownMenuItem asChild><Link href="/settings"><Settings /> Settings</Link></DropdownMenuItem>
        {user.isAdmin && <DropdownMenuItem asChild><Link href="/admin/team"><Users /> Team management</Link></DropdownMenuItem>}
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={() => router.post('/logout')}><LogOut /> Log out</DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

function SearchForm({ initial, autoFocus, onDone, className }: { initial: string; autoFocus?: boolean; onDone?: () => void; className?: string }) {
  const [q, setQ] = useState(initial)
  const submit = (e: FormEvent) => {
    e.preventDefault()
    router.get('/posts', q.trim() ? { q: q.trim() } : {}, { preserveState: true })
    onDone?.()
  }
  return (
    <form onSubmit={submit} role="search" className={cn('relative', className)}>
      <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
      <input
        type="search" value={q} onChange={(e) => setQ(e.target.value)} autoFocus={autoFocus}
        placeholder="Search essays" aria-label="Search posts"
        className="h-9 w-full rounded-full border border-transparent bg-muted pr-3 pl-9 text-sm outline-none transition-[background-color,border-color] placeholder:text-muted-foreground focus:border-ring/50 focus:bg-background"
      />
    </form>
  )
}

function HeaderLink({ item, active }: { item: NavEntry; active: boolean }) {
  return (
    <Link
      href={item.href} prefetch="hover" aria-current={active ? 'page' : undefined}
      className={cn('rounded-md px-3 py-1.5 text-sm text-muted-foreground transition-colors hover:text-foreground', active && 'font-medium text-foreground')}
    >
      {item.label}
    </Link>
  )
}

export default function AppLayout({ children, title, wide }: { children: ReactNode; title?: string; wide?: boolean }) {
  const page = usePage<SharedProps>()
  const { appName, auth } = page.props
  const user = auth.user
  const isActive = useActive()
  const [searching, setSearching] = useState(false)
  // Remounting on a new ?q= keeps the box in sync with Back, Clear and search links.
  const query = new URLSearchParams(page.url.split('?')[1] ?? '').get('q') ?? ''
  useRealtime(user?.id)
  useTheme(user?.theme ?? 'system')
  useFlashToasts(page.flash as Flash)
  useDocumentTitle(title, appName)

  const nav: NavEntry[] = [
    { href: '/posts', label: 'Latest', icon: Home },
    ...(user ? [
      { href: '/feed?mode=for-you', label: 'For you', icon: Home },
      { href: '/feed?mode=following', label: 'Following', icon: Users },
    ] : []),
    { href: '/users', label: 'People', icon: UserRound },
  ]
  const tabs: NavEntry[] = user
    ? [
      { href: '/posts', label: 'Latest', icon: Home },
      { href: '/feed?mode=following', label: 'Following', icon: Users },
      ...(user.verified ? [{ href: '/posts/create', label: 'Write', icon: PenLine }] : []),
      { href: '/users', label: 'People', icon: UserRound },
      { href: '/bookmarks', label: 'Saved', icon: Bookmark },
    ]
    : [nav[0]!, { href: '/users', label: 'People', icon: UserRound }, { href: '/login', label: 'Log in', icon: LogIn }]

  return (
    <div className="flex min-h-screen flex-col bg-background">
      <a href="#content" className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-50 focus:rounded-md focus:bg-background focus:px-3 focus:py-2 focus:text-sm focus:shadow">Skip to content</a>
      <header className="sticky top-0 z-30 border-b bg-background/85 backdrop-blur-md">
        <div className="mx-auto flex h-14 w-full max-w-5xl items-center gap-2 px-4 sm:px-6 lg:h-16">
          {searching ? (
            <>
              <SearchForm key={query} initial={query} autoFocus onDone={() => setSearching(false)} className="flex-1" />
              <Button variant="ghost" size="icon" className="size-11" onClick={() => setSearching(false)} aria-label="Close search"><X /></Button>
            </>
          ) : (
            <>
              <Link href="/posts" className="mr-4 rounded-md" aria-label={`${appName} home`}><BrandMark /></Link>
              <nav aria-label="Main navigation" className="hidden items-center lg:flex">
                {nav.map(item => <HeaderLink key={item.href} item={item} active={isActive(item.href)} />)}
              </nav>
              <div className="ml-auto flex items-center gap-1.5">
                <SearchForm key={query} initial={query} className="hidden w-52 lg:block" />
                <Button variant="ghost" size="icon" className="size-11 lg:hidden" onClick={() => setSearching(true)} aria-label="Search"><Search /></Button>
                {user?.verified && (
                  <Button asChild variant="ghost" size="sm" className="hidden h-9 text-muted-foreground hover:text-foreground lg:inline-flex">
                    <Link href="/posts/create"><PenLine /> Write</Link>
                  </Button>
                )}
                {user ? (
                  <><NotificationBell /><NotificationSound /><AccountMenu user={user}>
                    <button type="button" className="flex size-11 items-center justify-center rounded-full lg:size-10" aria-label="Account menu"><UserAvatar user={user} /></button>
                  </AccountMenu></>
                ) : (
                  <>
                    <Button asChild variant="ghost" size="sm" className="hidden h-9 sm:inline-flex"><Link href="/login">Log in</Link></Button>
                    <Button asChild size="sm" className="h-9 rounded-full px-4"><Link href="/register">Sign up</Link></Button>
                  </>
                )}
              </div>
            </>
          )}
        </div>
      </header>

      {user && !user.verified && (
        <div className="border-b bg-muted px-4 py-2.5 text-center text-sm">
          Verify your email to post, comment and like.{' '}
          <Link href="/email/verify" className="font-medium text-brand underline underline-offset-4">Resend link</Link>
        </div>
      )}

      <main
        id="content" key={page.url.split('?')[0]}
        className={cn('mx-auto w-full flex-1 animate-rise px-4 pt-8 pb-28 sm:px-6 lg:pt-14 lg:pb-20', wide ? 'max-w-5xl' : 'max-w-[44rem]')}
      >
        {children}
      </main>

      <footer className="hidden border-t py-8 text-center text-sm text-muted-foreground lg:block">
        <div className="mx-auto flex max-w-5xl items-center justify-between px-6">
          <span className="font-serif text-base text-foreground">{appName}</span>
          <span>&copy; {new Date().getFullYear()}</span>
        </div>
      </footer>

      {/* Mobile tab bar */}
      <nav aria-label="Tabs" className="fixed inset-x-0 bottom-0 z-30 border-t bg-background/92 pb-[env(safe-area-inset-bottom)] backdrop-blur-md lg:hidden">
        <div className="mx-auto flex max-w-md items-stretch justify-around">
          {tabs.map(({ href, label, icon: Icon }) => {
            const active = isActive(href)
            const write = href === '/posts/create'
            return (
              <Link
                key={href} href={href} aria-current={active ? 'page' : undefined}
                className={cn('flex min-h-14 flex-1 flex-col items-center justify-center gap-1 text-[11px] text-muted-foreground transition-colors active:scale-95', active && 'font-medium text-foreground')}
              >
                {write ? (
                  <span className="flex size-9 items-center justify-center rounded-full bg-foreground text-background"><Icon className="size-4" aria-hidden /></span>
                ) : (
                  <Icon className="size-5" strokeWidth={active ? 2.25 : 1.75} aria-hidden />
                )}
                <span className={cn(write && 'sr-only')}>{label}</span>
              </Link>
            )
          })}
        </div>
      </nav>

      <Toaster theme={user?.theme ?? 'system'} position="bottom-right" closeButton />
    </div>
  )
}
