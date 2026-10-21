<script lang="ts">
  import type { Snippet } from 'svelte'
  import { Link, router, usePage } from '@inertiajs/svelte'
  import { CalendarRange, CreditCard, LayoutDashboard, LogIn, LogOut, Receipt, ShieldCheck, UserRound } from '@lucide/svelte'
  import { Toaster } from 'svelte-sonner'
  import BrandMark from '@/components/BrandMark.svelte'
  import ThemeToggle from '@/components/ThemeToggle.svelte'
  import { useFlashToast } from '@/lib/flash-toast.svelte'
  import { useRealtime } from '@/lib/realtime.svelte'
  import { useTheme } from '@/lib/theme.svelte'
  import { cn } from '@/lib/utils'
  import type { AuthUser } from '@/types'

  /** Customer console shell: sidebar with Overview / Bookings / Payments / Insurance / Alerts. */
  let { children }: { children?: Snippet } = $props()

  const browser = typeof window !== 'undefined'
  const page = usePage()
  const user = $derived((page.props.auth as { user: AuthUser | null } | undefined)?.user ?? null)

  useTheme()
  useRealtime(() => user?.id)
  useFlashToast()

  $effect(() => {
    user?.id
    router.flushAll()
  })

  const logout = () => {
    router.flushAll()
    router.post('/logout')
  }

  const nav = [
    { href: '/account', label: 'Overview', icon: LayoutDashboard },
    { href: '/account/bookings', label: 'Bookings', icon: CalendarRange },
    { href: '/account/payments', label: 'Payments', icon: CreditCard },
    { href: '/account/insurance', label: 'Spot Cover', icon: ShieldCheck },
    { href: '/account/alerts', label: 'Alerts', icon: Receipt },
  ]

  const isActive = (href: string) => {
    const path = href.split('?')[0]!
    if (path === '/account') return page.url === '/account'
    return page.url === path || page.url.startsWith(`${path}?`) || page.url.startsWith(`${path}/`)
  }

  const linkClass = (href: string) => cn(
    'group flex items-center gap-3 rounded-xl px-3 py-2 text-sm transition-colors',
    isActive(href) ? 'bg-accent text-foreground font-medium' : 'text-muted-foreground hover:bg-accent/60 hover:text-foreground',
  )
</script>

<div class="bg-background flex min-h-screen">
  <!-- Sidebar -->
  <aside class="bg-card/40 fixed inset-y-0 left-0 z-30 hidden w-60 flex-col border-r lg:flex">
    <div class="px-4 pt-5 pb-4"><BrandMark size="sm" /></div>
    <nav class="flex flex-col gap-0.5 px-3" aria-label="Account">
      {#each nav as item (item.href)}
        <Link href={item.href} aria-current={isActive(item.href) ? 'page' : undefined} class={linkClass(item.href)}>
          <item.icon class="size-[18px]" strokeWidth={isActive(item.href) ? 2.25 : 1.75} aria-hidden="true" />
          {item.label}
        </Link>
      {/each}
    </nav>
    <nav class="mt-6 flex flex-col gap-0.5 border-t px-3 pt-4" aria-label="Profile">
      <p class="text-muted-foreground px-3 pb-1.5 text-[11px] font-semibold tracking-[0.14em] uppercase">Profile</p>
      <Link href="/account/profile" class={linkClass('/account/profile')} aria-current={page.url.startsWith('/account/profile') ? 'page' : undefined}>
        <UserRound class="size-[18px]" strokeWidth={1.75} aria-hidden="true" />
        Profile &amp; settings
      </Link>
      <Link href="/lots" class={linkClass('/lots')}><Receipt class="size-[18px]" strokeWidth={1.75} aria-hidden="true" /> Find parking</Link>
    </nav>
    <div class="mt-auto px-3 pb-4">
      {#if user}
        <div class="bg-card rounded-2xl border p-3">
          <p class="truncate text-sm font-medium">{user.name}</p>
          <p class="text-muted-foreground truncate text-xs">{user.email}</p>
          <button type="button" onclick={logout} class="text-muted-foreground hover:text-foreground mt-2 inline-flex items-center gap-1.5 text-xs hover:underline underline-offset-4">
            <LogOut class="size-3.5" aria-hidden="true" /> Log out
          </button>
        </div>
      {:else}
        <Link href="/login" class="text-muted-foreground hover:text-foreground inline-flex items-center gap-1.5 rounded-xl px-3 py-2 text-sm">
          <LogIn class="size-4" aria-hidden="true" /> Log in
        </Link>
      {/if}
    </div>
  </aside>

  <!-- Mobile top bar -->
  <header class="bg-background/80 fixed inset-x-0 top-0 z-30 border-b backdrop-blur-md lg:hidden">
    <div class="flex h-14 items-center justify-between px-4">
      <BrandMark size="sm" />
      <div class="flex items-center gap-2">
        <ThemeToggle />
        {#if user}
          <Link href="/account" class="text-muted-foreground hover:text-foreground rounded-full px-2 py-1 text-xs font-medium">{user.name.split(' ')[0]}</Link>
        {/if}
      </div>
    </div>
    <nav class="scrollbar-none flex gap-1 overflow-x-auto border-t px-3 py-2" aria-label="Account sections">
      {#each nav as item (item.href)}
        <Link
          href={item.href}
          aria-current={isActive(item.href) ? 'page' : undefined}
          class={cn(
            'text-muted-foreground flex shrink-0 items-center gap-1.5 rounded-full px-3 py-1.5 text-xs whitespace-nowrap',
            isActive(item.href) && 'bg-accent text-foreground font-medium',
          )}
        >
          <item.icon class="size-3.5" aria-hidden="true" />
          {item.label}
        </Link>
      {/each}
    </nav>
  </header>

  <div class="flex-1 lg:pl-60">
    <main class="animate-rise mx-auto w-full max-w-5xl px-4 pt-6 pb-24 sm:px-6 lg:pt-10 lg:pb-16">
      <div class="pt-16 lg:pt-0">
        {@render children?.()}
      </div>
    </main>
  </div>

  {#if browser}<Toaster position="bottom-right" closeButton />{/if}
</div>
