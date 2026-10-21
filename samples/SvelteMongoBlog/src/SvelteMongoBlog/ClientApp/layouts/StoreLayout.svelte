<script lang="ts">
  import type { Snippet } from 'svelte'
  import { Link, router, usePage } from '@inertiajs/svelte'
  import { LogIn, Menu, Search, ShieldCheck, UserRound, X } from '@lucide/svelte'
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

  /** Public storefront shell: sticky glass header, deck-plated hero band, mobile tab nav. */
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
    { type: 'item', label: 'Log out', icon: LogIn, onSelect: logout },
  ] as DropdownEntry[]))
</script>

<div class="bg-background flex min-h-screen flex-col">
  <!-- Sticky glass header -->
  <header class="bg-background/70 sticky top-0 z-40 border-b backdrop-blur-xl">
    <div class="mx-auto flex h-16 w-full max-w-6xl items-center gap-4 px-4 sm:px-6">
      <BrandMark />

      <nav class="ml-4 hidden items-center gap-1 md:flex" aria-label="Main">
        {#each nav as item (item.href)}
          <Link
            href={item.href}
            aria-current={isActive(item.href) ? 'page' : undefined}
            class={cn(
              'rounded-xl px-3 py-2 text-sm transition-colors',
              isActive(item.href) ? 'text-foreground bg-accent font-medium' : 'text-muted-foreground hover:text-foreground',
            )}
          >{item.label}</Link>
        {/each}
      </nav>

      <div class="ml-auto flex items-center gap-2">
        <form class="relative hidden sm:block" role="search" onsubmit={submitSearch}>
          <Search class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2" aria-hidden="true" />
          <input
            type="search"
            name="q"
            placeholder="Search lots…"
            aria-label="Search parking lots"
            class="bg-accent/60 placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-ring/30 h-9 w-44 rounded-xl border border-transparent pr-3 pl-9 text-sm transition-all outline-none focus-visible:w-56 focus-visible:ring-2"
          />
        </form>
        <ThemeToggle />
        {#if user}
          <DropdownMenu entries={accountEntries} align="end" triggerLabel="Account menu" triggerClass="rounded-full">
            {#snippet trigger()}
              <Avatar src={user.avatarUrl} fallback={user.name.split(/\s+/).map(p => p[0]).slice(0, 2).join('').toUpperCase()} class="size-8" fallbackClass="text-xs" />
            {/snippet}
          </DropdownMenu>
        {:else}
          <Button href="/login" variant="ghost" size="sm" class="rounded-xl">Log in</Button>
          <Button href="/register" size="sm" class="hidden rounded-xl sm:inline-flex">Get started</Button>
        {/if}
        <button
          type="button"
          class="hover:bg-accent inline-flex size-9 items-center justify-center rounded-xl md:hidden"
          aria-expanded={menuOpen}
          aria-label={menuOpen ? 'Close menu' : 'Open menu'}
          onclick={() => (menuOpen = !menuOpen)}
        >
          {#if menuOpen}<X class="size-5" aria-hidden="true" />{:else}<Menu class="size-5" aria-hidden="true" />{/if}
        </button>
      </div>
    </div>

    {#if menuOpen}
      <nav class="animate-rise border-t px-4 py-3 md:hidden" aria-label="Mobile">
        <div class="grid gap-1">
          {#each nav as item (item.href)}
            <Link href={item.href} class={cn('rounded-xl px-3 py-2.5 text-[15px]', isActive(item.href) ? 'bg-accent text-foreground font-medium' : 'text-muted-foreground')} onclick={() => (menuOpen = false)}>
              {item.label}
            </Link>
          {/each}
          {#if !user}
            <Link href="/register" class="rounded-xl px-3 py-2.5 text-[15px] font-medium" onclick={() => (menuOpen = false)}>Get started</Link>
          {/if}
        </div>
      </nav>
    {/if}
  </header>

  {#if user && !user.verified}
    <div class="bg-cta/15 text-cta-foreground border-b px-4 py-2.5 text-center text-sm">
      Verify your email to book spots.
      <Link href="/email/verify" class="font-medium underline underline-offset-4">Resend link</Link>
    </div>
  {/if}

  <main class="flex-1">
    {#key page.url.split('?')[0]}
      <div class="animate-rise">
        {@render children?.()}
      </div>
    {/key}
  </main>

  <footer class="border-t">
    <div class="text-muted-foreground mx-auto flex w-full max-w-6xl flex-col items-center justify-between gap-4 px-4 py-8 text-sm sm:flex-row sm:px-6">
      <div class="flex items-center gap-4">
        <BrandMark size="sm" />
        <span class="inline-flex items-center gap-1.5 text-xs">
          <ShieldCheck class="size-3.5" aria-hidden="true" /> Spot Cover on every lot
        </span>
      </div>
      <p class="text-xs">&copy; {new Date().getFullYear()} {appName} — premium parking, parked in seconds.</p>
    </div>
  </footer>

  {#if browser}<Toaster position="bottom-right" closeButton />{/if}
</div>
