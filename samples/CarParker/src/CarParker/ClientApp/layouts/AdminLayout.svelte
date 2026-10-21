<script lang="ts">
  import type { Snippet } from 'svelte'
  import { Link, router, usePage } from '@inertiajs/svelte'
  import {
    BarChart3, Bell, CalendarRange, CarFront, CircleDollarSign, LayoutDashboard, LogOut,
    ScrollText, ShieldCheck, ShieldUser, UsersRound, Car,
  } from '@lucide/svelte'
  import { Toaster } from 'svelte-sonner'
  import BrandMark from '@/components/BrandMark.svelte'
  import DropdownMenu, { type DropdownEntry } from '@/components/ui/dropdown-menu/DropdownMenu.svelte'
  import ThemeToggle from '@/components/ThemeToggle.svelte'
  import { useFlashToast } from '@/lib/flash-toast.svelte'
  import { useRealtime } from '@/lib/realtime.svelte'
  import { useTheme } from '@/lib/theme.svelte'
  import { cn } from '@/lib/utils'
  import type { AuthUser } from '@/types'

  /**
   * Admin console shell: sidebar with the full operations map, a needs-attention bell
   * (counts from the overview page, pulsing on BoardChanged) and theme toggle.
   */
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

  const nav: { href: string; label: string; icon: typeof Car }[] = [
    { href: '/admin', label: 'Overview', icon: LayoutDashboard },
    { href: '/admin/lots', label: 'Lots & spots', icon: Car },
    { href: '/admin/bookings', label: 'Bookings', icon: CalendarRange },
    { href: '/admin/sessions', label: 'Sessions', icon: CarFront },
    { href: '/admin/refunds', label: 'Refunds', icon: CircleDollarSign },
    { href: '/admin/insurance', label: 'Spot Cover', icon: ShieldCheck },
    { href: '/admin/incidents', label: 'Incidents', icon: ScrollText },
    { href: '/admin/users', label: 'Users', icon: UsersRound },
    { href: '/admin/analytics', label: 'Analytics', icon: BarChart3 },
  ]

  const isActive = (href: string) => {
    const path = href.split('?')[0]!
    if (path === '/admin') return page.url === '/admin'
    return page.url === path || page.url.startsWith(`${path}?`) || page.url.startsWith(`${path}/`)
  }

  const linkClass = (href: string) => cn(
    'group flex items-center gap-3 rounded-xl px-3 py-2 text-sm transition-colors',
    isActive(href) ? 'bg-accent text-foreground font-medium' : 'text-muted-foreground hover:bg-accent/60 hover:text-foreground',
  )

  // Needs-attention counts are on the overview props; other admin pages show the bell as a link hub.
  const attention = $derived((page.props as { overview?: { needsAttention?: Record<string, number> } }).overview?.needsAttention)
  const attentionCount = $derived(attention
    ? attention.refundsPending + attention.claimsPending + attention.overdueSessions + attention.openIncidents
    : 0)

  const bellEntries: DropdownEntry[] = [
    { type: 'label', label: 'Needs attention' },
    { type: 'separator' },
    { type: 'item', label: 'Pending refunds', href: '/admin/refunds' },
    { type: 'item', label: 'Open claims', href: '/admin/insurance' },
    { type: 'item', label: 'Open incidents', href: '/admin/incidents' },
    { type: 'item', label: 'Open sessions', href: '/admin/sessions' },
  ]
</script>

<div class="bg-background flex min-h-screen">
  <!-- Sidebar -->
  <aside class="bg-card/40 fixed inset-y-0 left-0 z-30 hidden w-64 flex-col border-r lg:flex">
    <div class="flex items-center justify-between px-4 pt-5 pb-4">
      <BrandMark size="sm" />
      <span class="bg-cta/15 text-cta-foreground rounded-full px-2 py-0.5 text-[10px] font-semibold tracking-[0.12em] uppercase">Admin</span>
    </div>
    <nav class="flex flex-1 flex-col gap-0.5 overflow-y-auto px-3" aria-label="Admin">
      {#each nav as item (item.href)}
        <Link href={item.href} aria-current={isActive(item.href) ? 'page' : undefined} class={linkClass(item.href)}>
          <item.icon class="size-[18px]" strokeWidth={isActive(item.href) ? 2.25 : 1.75} aria-hidden="true" />
          {item.label}
        </Link>
      {/each}
    </nav>
    <div class="px-3 pb-4">
      {#if user}
        <div class="bg-card flex items-center gap-2.5 rounded-2xl border p-3">
          <span class="bg-primary/10 text-primary flex size-8 shrink-0 items-center justify-center rounded-full" aria-hidden="true">
            <ShieldUser class="size-4" />
          </span>
          <div class="min-w-0 flex-1">
            <p class="truncate text-sm font-medium">{user.name}</p>
            <p class="text-muted-foreground truncate text-xs">{user.email}</p>
          </div>
          <button type="button" onclick={logout} aria-label="Log out" class="text-muted-foreground hover:text-destructive rounded-lg p-1.5 transition-colors">
            <LogOut class="size-4" aria-hidden="true" />
          </button>
        </div>
      {/if}
    </div>
  </aside>

  <!-- Top bar (mobile: full chrome; desktop: bell + theme) -->
  <header class="bg-background/80 fixed inset-x-0 top-0 z-30 border-b backdrop-blur-md lg:hidden">
    <div class="flex h-14 items-center gap-2 px-4">
      <BrandMark size="sm" />
      <span class="bg-cta/15 text-cta-foreground rounded-full px-2 py-0.5 text-[10px] font-semibold tracking-[0.12em] uppercase">Admin</span>
      <div class="ml-auto flex items-center gap-1">
        <ThemeToggle />
        <DropdownMenu entries={bellEntries} align="end" triggerLabel="Needs attention" triggerClass="rounded-xl">
          {#snippet trigger()}
            <span class="hover:bg-accent relative inline-flex size-9 items-center justify-center rounded-xl">
              <Bell class="size-[18px]" aria-hidden="true" />
              {#if attentionCount}
                <span class="bg-cta absolute top-1.5 right-1.5 size-2 rounded-full ring-2 ring-background" aria-hidden="true"></span>
              {/if}
            </span>
          {/snippet}
        </DropdownMenu>
        {#if user}
          <button type="button" onclick={logout} aria-label="Log out" class="hover:bg-accent inline-flex size-9 items-center justify-center rounded-xl">
            <LogOut class="size-4" aria-hidden="true" />
          </button>
        {/if}
      </div>
    </div>
  </header>

  <!-- Desktop floating bell -->
  <div class="fixed top-4 right-6 z-30 hidden lg:block">
    <div class="flex items-center gap-1.5">
      <ThemeToggle />
      <DropdownMenu entries={bellEntries} align="end" triggerLabel="Needs attention" triggerClass="rounded-xl">
        {#snippet trigger()}
          <span class="hover:bg-accent relative inline-flex size-9 items-center justify-center rounded-xl">
            <Bell class="size-[18px]" aria-hidden="true" />
            {#if attentionCount}
              <span class="bg-cta animate-pulse-soft absolute top-1.5 right-1.5 flex size-4 items-center justify-center rounded-full text-[9px] font-bold text-cta-foreground ring-2 ring-background">
                {attentionCount > 9 ? '9+' : attentionCount}
              </span>
            {/if}
          </span>
        {/snippet}
      </DropdownMenu>
    </div>
  </div>

  <div class="flex-1 lg:pl-64">
    <main class="animate-rise mx-auto w-full max-w-6xl px-4 pt-6 pb-24 sm:px-6 lg:pt-10 lg:pb-16">
      <div class="pt-16 lg:pt-0">
        {@render children?.()}
      </div>
    </main>
  </div>

  {#if browser}<Toaster position="bottom-right" closeButton />{/if}
</div>
