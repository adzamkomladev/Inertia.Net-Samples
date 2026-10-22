<script lang="ts" module>
  import type { Component } from 'svelte'

  export type NavItem = { href: string; label: string; icon: Component<Record<string, any>>; badge?: number }
  export type NavGroup = { label?: string; items: NavItem[] }
</script>

<script lang="ts">
  import type { Snippet } from 'svelte'
  import { Link, router, usePage } from '@inertiajs/svelte'
  import { LogOut } from '@lucide/svelte'
  import { Toaster } from 'svelte-sonner'
  import BrandMark from '@/components/BrandMark.svelte'
  import ThemeToggle from '@/components/ThemeToggle.svelte'
  import { useFlashToast } from '@/lib/flash-toast.svelte'
  import { useRealtime } from '@/lib/realtime.svelte'
  import { useTheme } from '@/lib/theme.svelte'
  import { cn } from '@/lib/utils'
  import type { AuthUser } from '@/types'

  /**
   * Sidebar shell shared by the customer account and the admin console: grouped nav with
   * an active marker on desktop, a scrolling tab strip on mobile, user + theme + log out
   * at the foot. `aside` renders extra controls next to the theme toggle.
   */
  let {
    nav,
    label,
    homeHref,
    width = 'max-w-5xl',
    aside,
    children,
  }: {
    nav: NavGroup[]
    label: string
    homeHref: string
    width?: string
    aside?: Snippet
    children?: Snippet
  } = $props()

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

  const isActive = (href: string) => {
    const path = href.split('?')[0]!
    if (path === homeHref) return page.url.split('?')[0] === path
    return page.url === path || page.url.startsWith(`${path}?`) || page.url.startsWith(`${path}/`)
  }

  const items = $derived(nav.flatMap(group => group.items))
</script>

<a href="#main" class="bg-primary text-primary-foreground sr-only z-50 rounded-lg px-4 py-2 focus:not-sr-only focus:fixed focus:top-3 focus:left-3">Skip to content</a>

<div class="bg-background flex min-h-screen">
  <aside class="bg-card fixed inset-y-0 left-0 z-30 hidden w-60 flex-col lg:flex">
    <div class="px-4 pt-5 pb-5">
      <BrandMark />
      <p class="text-muted-foreground mt-2 pl-0.5 text-xs font-semibold tracking-wider uppercase">{label}</p>
    </div>
    <nav class="flex flex-1 flex-col gap-5 overflow-y-auto px-3" aria-label={label}>
      {#each nav as group, index (index)}
        <div class="flex flex-col gap-0.5">
          {#if group.label}<p class="text-muted-foreground px-3 pb-1 text-xs font-semibold tracking-wider uppercase">{group.label}</p>{/if}
          {#each group.items as item (item.href)}
            <Link
              href={item.href}
              prefetch="hover"
              aria-current={isActive(item.href) ? 'page' : undefined}
              class={cn(
                'flex min-h-10 items-center gap-3 rounded-lg px-3 text-base font-medium transition-colors',
                isActive(item.href) ? 'bg-primary/10 text-primary font-semibold' : 'text-muted-foreground hover:bg-accent hover:text-foreground',
              )}
            >
              <item.icon class="size-[18px]" strokeWidth={isActive(item.href) ? 2.25 : 1.75} aria-hidden="true" />
              <span class="flex-1">{item.label}</span>
              {#if item.badge}<span class="bg-cta text-cta-foreground rounded-full px-1.5 text-xs font-bold tabular-nums">{item.badge}</span>{/if}
            </Link>
          {/each}
        </div>
      {/each}
    </nav>
    <div class="flex flex-col gap-2 p-3">
      {#if user}
        <div class="bg-muted flex items-center gap-2 rounded-lg p-2.5">
          <div class="min-w-0 flex-1">
            <p class="truncate text-sm font-semibold">{user.name}</p>
            <p class="text-muted-foreground truncate text-xs">{user.email}</p>
          </div>
          <ThemeToggle class="size-9" />
          {@render aside?.()}
          <button type="button" onclick={logout} aria-label="Log out" class="text-muted-foreground hover:text-destructive hover:bg-accent inline-flex size-9 items-center justify-center rounded-lg transition-colors">
            <LogOut class="size-4" aria-hidden="true" />
          </button>
        </div>
      {/if}
    </div>
  </aside>

  <header class="bg-card fixed inset-x-0 top-0 z-30 shadow-card lg:hidden">
    <div class="flex h-14 items-center justify-between gap-2 px-4">
      <BrandMark size="sm" />
      <div class="flex items-center">
        <ThemeToggle />
        {@render aside?.()}
        <button type="button" onclick={logout} aria-label="Log out" class="hover:bg-accent inline-flex size-11 items-center justify-center rounded-lg">
          <LogOut class="size-4" aria-hidden="true" />
        </button>
      </div>
    </div>
    <nav class="scrollbar-none flex gap-1 overflow-x-auto px-3 pb-2" aria-label={label}>
      {#each items as item (item.href)}
        <Link
          href={item.href}
          prefetch="hover"
          aria-current={isActive(item.href) ? 'page' : undefined}
          class={cn(
            'text-muted-foreground flex min-h-11 shrink-0 items-center gap-2 rounded-lg px-3.5 text-sm font-medium whitespace-nowrap',
            isActive(item.href) && 'bg-primary/10 text-primary font-semibold',
          )}
        >
          <item.icon class="size-4" aria-hidden="true" />
          {item.label}
          {#if item.badge}<span class="bg-cta text-cta-foreground rounded-full px-1.5 text-xs font-bold tabular-nums">{item.badge}</span>{/if}
        </Link>
      {/each}
    </nav>
  </header>

  <div class="min-w-0 flex-1 lg:pl-60">
    <main id="main" class={cn('mx-auto w-full px-4 pt-[7.5rem] pb-20 sm:px-6 lg:px-8 lg:pt-10 lg:pb-16', width)}>
      {@render children?.()}
    </main>
  </div>

  {#if browser}<Toaster position="bottom-right" closeButton />{/if}
</div>
