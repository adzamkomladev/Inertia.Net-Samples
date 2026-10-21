import { Link, router, usePage } from '@inertiajs/react'
import {
  Archive, BarChart3, Bookmark, Compass, FileText, Home, LogIn, LogOut, MoreHorizontal,
  PenLine, Search, Settings, UserRound, Users, X,
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

/** Serif monogram used as the logo. */
export function BrandMark({ className }: { className?: string }) {
  const { appName } = usePage<SharedProps>().props
  return (
    <span className={cn('flex size-8 items-center justify-center rounded-lg bg-foreground font-serif text-lg leading-none font-semibold text-background', className)} aria-hidden>
      {appName.charAt(0)}
    </span>
  )
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

function SidebarLink({ item, active }: { item: NavEntry; active: boolean }) {
  const Icon = item.icon
  return (
    <Link
      href={item.href} aria-current={active ? 'page' : undefined}
      className={cn(
        'group flex items-center gap-3 rounded-lg px-3 py-2 text-[15px] text-muted-foreground transition-colors hover:bg-accent hover:text-foreground',
        active && 'bg-accent font-medium text-foreground',
      )}
    >
      <Icon className={cn('size-[18px] transition-transform group-active:scale-90', active && 'text-brand')} strokeWidth={active ? 2.25 : 1.75} aria-hidden />
      {item.label}
    </Link>
  )
}

function UserAvatar({ user, className }: { user: AuthUser; className?: string }) {
  return (
    <Avatar className={cn('size-8', className)}>
      {user.avatarUrl && <AvatarImage src={user.avatarUrl} alt="" />}
      <AvatarFallback className="text-xs">{initials(user.name)}</AvatarFallback>
    </Avatar>
  )
}

function AccountMenu({ user, children, align = 'start' }: { user: AuthUser; children: ReactNode; align?: 'start' | 'end' }) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>{children}</DropdownMenuTrigger>
      <DropdownMenuContent align={align} side="top" className="w-60">
        <DropdownMenuLabel className="font-normal">
          <div className="truncate text-sm font-medium">{user.name}</div>
          <div className="truncate text-xs text-muted-foreground">{user.email}</div>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem asChild><Link href={`/users/${user.id}`}><UserRound /> Profile</Link></DropdownMenuItem>
        <DropdownMenuItem asChild className="lg:hidden"><Link href="/me/posts"><FileText /> My posts</Link></DropdownMenuItem>
        <DropdownMenuItem asChild className="lg:hidden"><Link href="/analytics"><BarChart3 /> Analytics</Link></DropdownMenuItem>
        <DropdownMenuItem asChild><Link href="/me/archives"><Archive /> Archives</Link></DropdownMenuItem>
        <DropdownMenuItem asChild><Link href="/settings"><Settings /> Settings</Link></DropdownMenuItem>
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={() => router.post('/logout')}><LogOut /> Log out</DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

function SearchForm({ initial, autoFocus, onDone }: { initial: string; autoFocus?: boolean; onDone?: () => void }) {
  const [q, setQ] = useState(initial)
  const submit = (e: FormEvent) => {
    e.preventDefault()
    router.get('/posts', q.trim() ? { q: q.trim() } : {}, { preserveState: true })
    onDone?.()
  }
  return (
    <form onSubmit={submit} role="search" className="relative">
      <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
      <input
        type="search" value={q} onChange={(e) => setQ(e.target.value)} autoFocus={autoFocus}
        placeholder="Search" aria-label="Search posts"
        className="h-9 w-full rounded-lg border border-transparent bg-muted pr-3 pl-9 text-sm outline-none transition-colors placeholder:text-muted-foreground focus:border-ring/40 focus:bg-background"
      />
    </form>
  )
}

export default function AppLayout({ children, title }: { children: ReactNode; title?: string }) {
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

  const primary: NavEntry[] = [
    { href: '/posts', label: 'Home', icon: Home },
    ...(user ? [
      { href: '/feed?mode=for-you', label: 'For you', icon: Compass },
      { href: '/feed?mode=following', label: 'Following', icon: Users },
    ] : []),
    { href: '/users', label: 'People', icon: UserRound },
  ]
  const library: NavEntry[] = user ? [
    { href: '/bookmarks', label: 'Bookmarks', icon: Bookmark },
    { href: '/me/posts', label: 'My posts', icon: FileText },
    { href: '/analytics', label: 'Analytics', icon: BarChart3 },
  ] : []
  const tabs: NavEntry[] = user
    ? [primary[0]!, primary[1]!, ...(user.verified ? [{ href: '/posts/create', label: 'Write', icon: PenLine }] : []), { href: '/users', label: 'People', icon: UserRound }, { href: '/bookmarks', label: 'Saved', icon: Bookmark }]
    : [primary[0]!, { href: '/users', label: 'People', icon: UserRound }, { href: '/login', label: 'Log in', icon: LogIn }]

  return (
    <div className="min-h-screen bg-background">
      {/* Desktop sidebar */}
      <aside className="fixed inset-y-0 left-0 z-30 hidden w-64 flex-col border-r bg-background px-3 py-4 lg:flex">
        <Link href="/posts" className="mb-5 flex items-center gap-2.5 rounded-lg px-2 py-1 text-[17px] font-semibold tracking-tight">
          <BrandMark />
          {appName}
        </Link>
        <div className="mb-4 px-1"><SearchForm key={query} initial={query} /></div>
        <nav aria-label="Main navigation" className="flex flex-col gap-0.5">
          {primary.map(item => <SidebarLink key={item.href} item={item} active={isActive(item.href)} />)}
        </nav>
        {library.length > 0 && (
          <nav aria-label="Your library" className="mt-6 flex flex-col gap-0.5">
            <p className="px-3 pb-1.5 text-xs font-medium text-muted-foreground">Library</p>
            {library.map(item => <SidebarLink key={item.href} item={item} active={isActive(item.href)} />)}
          </nav>
        )}
        {user?.verified && (
          <Button asChild className="mt-6 h-10 rounded-lg text-[15px] transition-transform active:scale-[0.98]">
            <Link href="/posts/create"><PenLine /> Write a post</Link>
          </Button>
        )}

        <div className="mt-auto">
          {user ? (
            <AccountMenu user={user}>
              <button type="button" className="flex w-full items-center gap-3 rounded-lg p-2 text-left transition-colors hover:bg-accent" aria-label="Account menu">
                <UserAvatar user={user} />
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm font-medium">{user.name}</span>
                  <span className="block truncate text-xs text-muted-foreground">{user.email}</span>
                </span>
                <MoreHorizontal className="size-4 text-muted-foreground" aria-hidden />
              </button>
            </AccountMenu>
          ) : (
            <div className="flex flex-col gap-2 rounded-xl border p-3">
              <p className="text-sm font-medium">Join the conversation</p>
              <p className="text-xs leading-5 text-muted-foreground">Follow writers, like posts and build your own feed.</p>
              <Button asChild size="sm" className="mt-1 rounded-lg"><Link href="/register">Create account</Link></Button>
              <Button asChild size="sm" variant="ghost" className="rounded-lg"><Link href="/login">Log in</Link></Button>
            </div>
          )}
        </div>
      </aside>

      {/* Mobile top bar */}
      <header className="sticky top-0 z-30 border-b bg-background/80 backdrop-blur-md lg:hidden">
        <div className="flex h-14 items-center gap-3 px-4">
          {searching ? (
            <>
              <div className="flex-1"><SearchForm key={query} initial={query} autoFocus onDone={() => setSearching(false)} /></div>
              <Button variant="ghost" size="icon-sm" onClick={() => setSearching(false)} aria-label="Close search"><X /></Button>
            </>
          ) : (
            <>
              <Link href="/posts" className="flex items-center gap-2 font-semibold tracking-tight">
                <BrandMark className="size-7 text-base" />
                {appName}
              </Link>
              <Button variant="ghost" size="icon-sm" className="ml-auto rounded-full" onClick={() => setSearching(true)} aria-label="Search"><Search /></Button>
              {user ? (
                <AccountMenu user={user} align="end">
                  <button type="button" className="rounded-full" aria-label="Account menu"><UserAvatar user={user} className="size-7" /></button>
                </AccountMenu>
              ) : (
                <Button asChild size="sm" className="rounded-full"><Link href="/register">Sign up</Link></Button>
              )}
            </>
          )}
        </div>
      </header>

      <div className="lg:pl-64">
        {user && !user.verified && (
          <div className="border-b bg-brand/8 px-4 py-2.5 text-center text-sm">
            Verify your email to post, comment and like.{' '}
            <Link href="/email/verify" className="font-medium text-brand underline-offset-4 hover:underline">Resend link</Link>
          </div>
        )}
        <main key={page.url.split('?')[0]} className="mx-auto w-full max-w-5xl animate-rise px-4 pt-6 pb-28 sm:px-6 lg:px-10 lg:pt-10 lg:pb-16">
          {children}
        </main>
      </div>

      {/* Mobile tab bar */}
      <nav aria-label="Tabs" className="fixed inset-x-0 bottom-0 z-30 border-t bg-background/90 pb-[env(safe-area-inset-bottom)] backdrop-blur-md lg:hidden">
        <div className="mx-auto flex max-w-md items-stretch justify-around">
          {tabs.map(({ href, label, icon: Icon }) => {
            const active = isActive(href)
            const write = href === '/posts/create'
            return (
              <Link
                key={href} href={href} aria-current={active ? 'page' : undefined}
                className={cn('flex flex-1 flex-col items-center gap-1 py-2 text-[11px] text-muted-foreground transition-colors active:scale-95', active && 'text-foreground')}
              >
                {write ? (
                  <span className="flex size-8 items-center justify-center rounded-full bg-foreground text-background"><Icon className="size-4" aria-hidden /></span>
                ) : (
                  <Icon className={cn('size-5', active && 'text-brand')} strokeWidth={active ? 2.25 : 1.75} aria-hidden />
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
