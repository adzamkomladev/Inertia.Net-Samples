<script lang="ts">
  import type { Snippet } from 'svelte'
  import { Link, router, usePage } from '@inertiajs/svelte'
  import LayoutDashboard from '@lucide/svelte/icons/layout-dashboard'
  import LogOut from '@lucide/svelte/icons/log-out'
  import Menu from '@lucide/svelte/icons/menu'
  import Search from '@lucide/svelte/icons/search'
  import UserRound from '@lucide/svelte/icons/user-round'
  import SidePanel from '@/components/SidePanel.svelte'
  import NavigationSkeleton from '@/components/NavigationSkeleton.svelte'
  import BrandMark from '@/components/BrandMark.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import DropdownMenu, { type DropdownEntry } from '@/components/ui/dropdown-menu/DropdownMenu.svelte'
  import Avatar from '@/components/ui/avatar/Avatar.svelte'
  import NotificationSound from '@/components/NotificationSound.svelte'
  import NotificationBell from '@/components/NotificationBell.svelte'
  import ThemeToggle from '@/components/ThemeToggle.svelte'
  import { usePendingVisit } from '@/lib/pending-visit.svelte'
  import { useFlashToast } from '@/lib/flash-toast.svelte'
  import { useRealtime } from '@/lib/realtime.svelte'
  import { useTheme } from '@/lib/theme.svelte'
  import { cn, initials } from '@/lib/utils'
  import type { AuthUser } from '@/types'
  import { routes } from '@/types/routes'

  /** Public shell: solid header with search, account menu, footer. */
  let { children }: { children?: Snippet } = $props()

  const page = usePage()
  const user = $derived((page.props.auth as { user: AuthUser | null } | undefined)?.user ?? null)
  const appName = $derived((page.props as { appName?: string }).appName ?? 'CarParker')

  useTheme()
  useRealtime(() => user?.id)
  useFlashToast()

  let menuOpen = $state(false)
  const pending = usePendingVisit()
  // The drawer closes on every navigation, however the visit started.
  $effect(() => {
    page.url
    menuOpen = false
  })

  /** Staff get a one-click way back to their console from the public pages. */
  const staffConsole = $derived(user?.role === 'admin' ? { href: routes.admin.index.url(), label: 'Admin console' } : user?.role === 'guard' ? { href: routes.guard.console.url(), label: 'Guard console' } : null)

  // Typed routes are generated from the named endpoints (ClientApp/types/routes.ts).
  const nav = [
    { href: routes.lots.index.url(), label: 'Find parking' },
    { href: routes.pages.about.url(), label: 'How it works' },
    { href: routes.pages.policies.url(), label: 'Policies' },
  ]

  const isActive = (href: string) => {
    const path = href.split('?')[0]!
    return page.url === path || page.url.startsWith(`${path}?`) || page.url.startsWith(`${path}/`)
  }

  // Prefetched pages carry the old viewer's props: drop them when identity changes.
  const identity = $derived(user?.id ?? '')
  $effect(() => {
    identity
    router.flushAll()
  })

  const logout = () => {
    router.flushAll()
    router.post(routes.logout())
  }

  const submitSearch = (event: SubmitEvent) => {
    event.preventDefault()
    const value = new FormData(event.currentTarget as HTMLFormElement).get('q')
    router.get(routes.lots.index.url(), value && String(value).trim() ? { q: String(value).trim() } : {})
  }

  const accountLinks = [
    { href: routes.account.index.url(), label: 'My parking' },
    { href: routes.account.bookings.index.url(), label: 'Bookings' },
    { href: routes.account.payments.url(), label: 'Payments' },
    { href: routes.account.insurance.url(), label: 'Spot Cover' },
  ]

  const accountEntries = $derived(([
    { type: 'label', label: user?.name ?? '', sublabel: user?.email ?? '' },
    { type: 'separator' },
    { type: 'item', label: 'My parking', icon: UserRound, href: routes.account.index.url() },
    { type: 'item', label: 'Bookings', href: routes.account.bookings.index.url() },
    { type: 'item', label: 'Payments', href: routes.account.payments.url() },
    { type: 'item', label: 'Spot Cover', href: routes.account.insurance.url() },
    ...(user?.role === 'admin' ? [{ type: 'item', label: 'Admin console', href: routes.admin.index.url() }] : []),
    ...(user?.role === 'guard' ? [{ type: 'item', label: 'Guard console', href: routes.guard.console.url() }] : []),
    { type: 'separator' },
    { type: 'item', label: 'Log out', icon: LogOut, onSelect: logout },
  ] as DropdownEntry[]))
</script>

<a href="#main" class="bg-primary text-primary-foreground sr-only z-50 rounded-lg px-4 py-2 focus:not-sr-only focus:fixed focus:top-3 focus:left-3">Skip to content</a>

<div class="bg-background flex min-h-screen flex-col">
  <header class="bg-card pt-safe sticky top-0 z-40 shadow-card">
    <div class="mx-auto flex h-16 w-full max-w-6xl items-center gap-3 px-4 sm:px-6">
      <BrandMark />

      <nav class="ml-6 hidden items-center gap-1 md:flex" aria-label="Main">
        {#each nav as item (item.href)}
          <Link
            href={item.href}
            prefetch="hover"
            aria-current={isActive(item.href) ? 'page' : undefined}
            class={cn(
              'rounded-lg px-3 py-2 text-base font-medium transition-colors',
              isActive(item.href) ? 'bg-accent text-foreground' : 'text-muted-foreground hover:text-foreground',
            )}
          >{item.label}</Link>
        {/each}
      </nav>

      <div class="ml-auto flex items-center gap-1.5">
        <form class="relative hidden lg:block" role="search" onsubmit={submitSearch}>
          <Search class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2" aria-hidden="true" />
          <input
            type="search"
            name="q"
            placeholder="Area or lot name"
            aria-label="Search parking lots"
            class="bg-muted placeholder:text-muted-foreground focus-visible:ring-ring h-10 w-52 rounded-lg pr-3 pl-9 text-sm outline-none focus-visible:ring-2"
          />
        </form>
        {#if staffConsole}
          <Button href={staffConsole.href} variant="outline" size="sm" class="hidden sm:inline-flex">
            <LayoutDashboard class="size-4" aria-hidden="true" /> {staffConsole.label}
          </Button>
        {/if}
        <NotificationBell /><NotificationSound /><ThemeToggle />
        {#if user}
          <DropdownMenu entries={accountEntries} align="end" triggerLabel="Account menu" triggerClass="rounded-full">
            {#snippet trigger()}
              <Avatar src={user.avatarUrl} fallback={initials(user.name)} class="size-10" fallbackClass="text-sm" />
            {/snippet}
          </DropdownMenu>
        {:else}
          <Button href="/login" variant="ghost" size="sm">Log in</Button>
          <Button href="/register" size="sm" class="hidden sm:inline-flex">Sign up</Button>
        {/if}
        <button
          type="button"
          class="hover:bg-accent inline-flex size-11 items-center justify-center rounded-lg md:hidden"
          aria-haspopup="dialog"
          aria-expanded={menuOpen}
          aria-label="Open menu"
          onclick={() => (menuOpen = true)}
        >
          <Menu class="size-5" aria-hidden="true" />
        </button>
      </div>
    </div>
  </header>

  {#if user && !user.verified}
    <div class="bg-cta/20 text-foreground px-4 py-2.5 text-center text-sm">
      Verify your email to book spots.
      <Link href="/email/verify" class="font-semibold underline underline-offset-4">Resend link</Link>
    </div>
  {/if}

  <main id="main" class="flex-1">
    {#if pending.current}<NavigationSkeleton kind={pending.current.kind} />{/if}
    <div class={pending.current ? 'hidden' : 'contents'}>{@render children?.()}</div>
  </main>

  <footer class="bg-card pb-safe mt-16">
    <div class="text-muted-foreground mx-auto flex w-full max-w-6xl flex-col gap-4 px-4 py-8 text-sm sm:flex-row sm:items-center sm:justify-between sm:px-6">
      <BrandMark size="sm" />
      <nav class="flex gap-5" aria-label="Footer">
        {#each nav as item (item.href)}
          <Link href={item.href} class="hover:text-foreground">{item.label}</Link>
        {/each}
      </nav>
      <p>&copy; {new Date().getFullYear()} {appName}</p>
    </div>
  </footer>

</div>

<SidePanel bind:open={menuOpen} side="left" title="Menu" description="Search lots and move around CarParker.">
  <div class="pb-safe grid gap-5">
    <form class="relative" role="search" onsubmit={submitSearch}>
      <Search class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2" aria-hidden="true" />
      <input type="search" name="q" enterkeyhint="search" placeholder="Area or lot name" aria-label="Search parking lots" class="bg-muted placeholder:text-muted-foreground focus-visible:ring-ring h-11 w-full rounded-lg pr-3 pl-9 text-base outline-none focus-visible:ring-2" />
    </form>
    <nav class="grid gap-1" aria-label="Mobile">
      {#each nav as item (item.href)}
        <Link href={item.href} aria-current={isActive(item.href) ? 'page' : undefined} class={cn('flex min-h-11 items-center rounded-lg px-3 text-base', isActive(item.href) ? 'bg-accent text-foreground font-semibold' : 'text-muted-foreground')}>{item.label}</Link>
      {/each}
      {#if user}
        <p class="text-muted-foreground mt-3 px-3 text-xs font-semibold tracking-wider uppercase">Your account</p>
        {#each accountLinks as item (item.href)}
          <Link href={item.href} class="text-muted-foreground flex min-h-11 items-center rounded-lg px-3 text-base">{item.label}</Link>
        {/each}
      {/if}
      {#if staffConsole}
        <Link href={staffConsole.href} class="flex min-h-11 items-center gap-2 rounded-lg px-3 text-base font-semibold"><LayoutDashboard class="size-4" aria-hidden="true" /> {staffConsole.label}</Link>
      {/if}
    </nav>
    {#if user}
      <button type="button" onclick={logout} class="hover:bg-accent text-destructive flex min-h-11 items-center gap-2 rounded-lg px-3 text-base font-medium"><LogOut class="size-4" aria-hidden="true" /> Log out</button>
    {:else}
      <div class="grid grid-cols-2 gap-2">
        <Button href="/login" variant="outline" size="lg">Log in</Button>
        <Button href="/register" size="lg">Sign up</Button>
      </div>
    {/if}
  </div>
</SidePanel>
