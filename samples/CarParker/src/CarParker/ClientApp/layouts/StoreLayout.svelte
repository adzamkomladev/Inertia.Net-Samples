<script lang="ts">
  import type { Snippet } from 'svelte'
  import { Link, router, usePage } from '@inertiajs/svelte'
  import { LogOut, Menu, Search, UserRound, X } from '@lucide/svelte'
  import { Toaster } from 'svelte-sonner'
  import BrandMark from '@/components/BrandMark.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import DropdownMenu, { type DropdownEntry } from '@/components/ui/dropdown-menu/DropdownMenu.svelte'
  import Avatar from '@/components/ui/avatar/Avatar.svelte'
  import ThemeToggle from '@/components/ThemeToggle.svelte'
  import { useFlashToast } from '@/lib/flash-toast.svelte'
  import { useRealtime } from '@/lib/realtime.svelte'
  import { useTheme } from '@/lib/theme.svelte'
  import { cn } from '@/lib/utils'
  import type { AuthUser } from '@/types'

  /** Public shell: solid header with search, account menu, footer. */
  let { children }: { children?: Snippet } = $props()

  const browser = typeof window !== 'undefined'
  const page = usePage()
  const user = $derived((page.props.auth as { user: AuthUser | null } | undefined)?.user ?? null)
  const appName = $derived((page.props as { appName?: string }).appName ?? 'CarParker')

  useTheme()
  useRealtime(() => user?.id)
  useFlashToast()

  let menuOpen = $state(false)

  const nav = [
    { href: '/lots', label: 'Find parking' },
    { href: '/about', label: 'How it works' },
    { href: '/policies', label: 'Policies' },
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
    router.post('/logout')
  }

  const submitSearch = (event: SubmitEvent) => {
    event.preventDefault()
    const value = new FormData(event.currentTarget as HTMLFormElement).get('q')
    router.get('/lots', value && String(value).trim() ? { q: String(value).trim() } : {})
  }

  const accountEntries = $derived(([
    { type: 'label', label: user?.name ?? '', sublabel: user?.email ?? '' },
    { type: 'separator' },
    { type: 'item', label: 'My parking', icon: UserRound, href: '/account' },
    { type: 'item', label: 'Bookings', href: '/account/bookings' },
    { type: 'item', label: 'Payments', href: '/account/payments' },
    { type: 'item', label: 'Spot Cover', href: '/account/insurance' },
    ...(user?.role === 'admin' ? [{ type: 'item', label: 'Admin console', href: '/admin' }] : []),
    ...(user?.role === 'guard' ? [{ type: 'item', label: 'Guard console', href: '/guard' }] : []),
    { type: 'separator' },
    { type: 'item', label: 'Log out', icon: LogOut, onSelect: logout },
  ] as DropdownEntry[]))
</script>

<a href="#main" class="bg-primary text-primary-foreground sr-only z-50 rounded-lg px-4 py-2 focus:not-sr-only focus:fixed focus:top-3 focus:left-3">Skip to content</a>

<div class="bg-background flex min-h-screen flex-col">
  <header class="bg-card sticky top-0 z-40 shadow-card">
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
        <ThemeToggle />
        {#if user}
          <DropdownMenu entries={accountEntries} align="end" triggerLabel="Account menu" triggerClass="rounded-full">
            {#snippet trigger()}
              <Avatar src={user.avatarUrl} fallback={user.name.split(/\s+/).map(p => p[0]).slice(0, 2).join('').toUpperCase()} class="size-10" fallbackClass="text-sm" />
            {/snippet}
          </DropdownMenu>
        {:else}
          <Button href="/login" variant="ghost" size="sm">Log in</Button>
          <Button href="/register" size="sm" class="hidden sm:inline-flex">Sign up</Button>
        {/if}
        <button
          type="button"
          class="hover:bg-accent inline-flex size-11 items-center justify-center rounded-lg md:hidden"
          aria-expanded={menuOpen}
          aria-controls="mobile-nav"
          aria-label={menuOpen ? 'Close menu' : 'Open menu'}
          onclick={() => (menuOpen = !menuOpen)}
        >
          {#if menuOpen}<X class="size-5" aria-hidden="true" />{:else}<Menu class="size-5" aria-hidden="true" />{/if}
        </button>
      </div>
    </div>

    {#if menuOpen}
      <nav id="mobile-nav" class="border-t px-4 py-3 md:hidden" aria-label="Mobile">
        <form class="relative mb-2" role="search" onsubmit={(event) => { submitSearch(event); menuOpen = false }}>
          <Search class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2" aria-hidden="true" />
          <input type="search" name="q" placeholder="Area or lot name" aria-label="Search parking lots" class="bg-muted placeholder:text-muted-foreground h-11 w-full rounded-lg pr-3 pl-9 text-base outline-none" />
        </form>
        <div class="grid gap-1">
          {#each nav as item (item.href)}
            <Link href={item.href} class={cn('flex min-h-11 items-center rounded-lg px-3 text-base', isActive(item.href) ? 'bg-accent text-foreground font-semibold' : 'text-muted-foreground')} onclick={() => (menuOpen = false)}>
              {item.label}
            </Link>
          {/each}
          {#if !user}
            <Link href="/register" class="flex min-h-11 items-center rounded-lg px-3 text-base font-semibold" onclick={() => (menuOpen = false)}>Sign up</Link>
          {/if}
        </div>
      </nav>
    {/if}
  </header>

  {#if user && !user.verified}
    <div class="bg-cta/20 text-foreground px-4 py-2.5 text-center text-sm">
      Verify your email to book spots.
      <Link href="/email/verify" class="font-semibold underline underline-offset-4">Resend link</Link>
    </div>
  {/if}

  <main id="main" class="flex-1">
    {@render children?.()}
  </main>

  <footer class="bg-card mt-16">
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

  {#if browser}<Toaster position="bottom-right" closeButton />{/if}
</div>
