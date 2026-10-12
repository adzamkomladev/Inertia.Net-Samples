<script lang="ts">
  import type { Component, Snippet } from 'svelte'
  import { Link, router, usePage } from '@inertiajs/svelte'
  import {
    Archive, BarChart3, Bookmark, Compass, FileText, Home, LogIn, LogOut, MoreHorizontal,
    PenLine, Search, Settings, UserRound, Users, X,
  } from '@lucide/svelte'
  import { Toaster } from 'svelte-sonner'
  import Avatar from '@/components/ui/avatar/Avatar.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import DropdownMenu, { type DropdownEntry } from '@/components/ui/dropdown-menu/DropdownMenu.svelte'
  import SearchBox from '@/components/SearchBox.svelte'
  import { useFlashToast } from '@/lib/flash-toast.svelte'
  import { useRealtime } from '@/lib/realtime.svelte'
  import { useTheme } from '@/lib/theme.svelte'
  import { cn } from '@/lib/utils'
  import type { AuthUser } from '@/types'

  let { children }: { children?: Snippet } = $props()

  type NavEntry = { href: string; label: string; icon: Component<Record<string, any>> }

  const browser = typeof window !== 'undefined'
  const page = usePage()
  const user = $derived((page.props.auth as { user: AuthUser | null } | undefined)?.user ?? null)
  const appName = $derived((page.props as { appName?: string }).appName ?? 'Inkwell')
  const initials = $derived(user?.name.split(/\s+/).map(p => p[0]).slice(0, 2).join('').toUpperCase() ?? '')
  let q = $state((page.props.filters as { q?: string } | undefined)?.q ?? '')
  let searching = $state(false)
  // Keep the search box in sync with the URL (back/forward, links, clearing the query).
  $effect(() => {
    q = (page.props.filters as { q?: string } | undefined)?.q ?? ''
  })

  useTheme()
  useRealtime(() => user?.id)
  useFlashToast()

  // Prefetched pages carry the old viewer's props (liked, canEdit, auth): drop them whenever identity or verification changes.
  let identity = $derived(`${user?.id ?? ''}:${user?.verified ?? ''}`)
  $effect(() => {
    identity
    router.flushAll()
  })

  const logout = () => {
    router.flushAll()
    router.post('/logout')
  }

  const isActive = (href: string) => {
    const url = page.url
    const path = href.split('?')[0]!
    if (href.includes('?')) return url.split('&')[0] === href
    if (path === '/posts') return url === '/posts' || url.startsWith('/posts?') || url === '/'
    return url === href || url.startsWith(`${path}?`) || url.startsWith(`${path}/`)
  }

  const primary = $derived<NavEntry[]>([
    { href: '/posts', label: 'Home', icon: Home },
    ...(user
      ? [
          { href: '/feed?mode=for-you', label: 'For you', icon: Compass },
          { href: '/feed?mode=following', label: 'Following', icon: Users },
        ]
      : []),
    { href: '/users', label: 'People', icon: UserRound },
  ])
  const library = $derived<NavEntry[]>(user
    ? [
        { href: '/bookmarks', label: 'Bookmarks', icon: Bookmark },
        { href: '/me/posts', label: 'My posts', icon: FileText },
        { href: '/analytics', label: 'Analytics', icon: BarChart3 },
      ]
    : [])
  const tabs = $derived<NavEntry[]>(user
    ? [
        primary[0]!,
        primary[1]!,
        ...(user.verified ? [{ href: '/posts/create', label: 'Write', icon: PenLine }] : []),
        { href: '/users', label: 'People', icon: UserRound },
        { href: '/bookmarks', label: 'Saved', icon: Bookmark },
      ]
    : [primary[0]!, { href: '/users', label: 'People', icon: UserRound }, { href: '/login', label: 'Log in', icon: LogIn }])
  const linkClass = (href: string) => cn(
    'group text-muted-foreground hover:bg-accent hover:text-foreground flex items-center gap-3 rounded-lg px-3 py-2 text-[15px] transition-colors',
    isActive(href) && 'bg-accent text-foreground font-medium',
  )

  const accountEntries = $derived<DropdownEntry[]>([
    { type: 'label', label: user?.name ?? '', sublabel: user?.email ?? '' },
    { type: 'separator' },
    { type: 'item', label: 'Profile', icon: UserRound, href: `/users/${user?.id}` },
    { type: 'item', label: 'Archives', icon: Archive, href: '/me/archives' },
    { type: 'item', label: 'Settings', icon: Settings, href: '/settings/preferences' },
    { type: 'separator' },
    { type: 'item', label: 'Log out', icon: LogOut, onSelect: logout },
  ])
  const mobileEntries = $derived<DropdownEntry[]>([
    { type: 'label', label: user?.name ?? '', sublabel: user?.email ?? '' },
    { type: 'separator' },
    { type: 'item', label: 'Profile', icon: UserRound, href: `/users/${user?.id}` },
    { type: 'item', label: 'My posts', icon: FileText, href: '/me/posts' },
    { type: 'item', label: 'Analytics', icon: BarChart3, href: '/analytics' },
    { type: 'item', label: 'Archives', icon: Archive, href: '/me/archives' },
    { type: 'item', label: 'Settings', icon: Settings, href: '/settings/preferences' },
    { type: 'separator' },
    { type: 'item', label: 'Log out', icon: LogOut, onSelect: logout },
  ])
</script>

<div class="bg-background min-h-screen">
  <!-- Desktop sidebar -->
  <aside class="bg-background fixed inset-y-0 left-0 z-30 hidden w-64 flex-col border-r px-3 py-4 lg:flex">
    <Link href="/posts" class="mb-5 flex items-center gap-2.5 rounded-lg px-2 py-1 text-[17px] font-semibold tracking-tight">
      <span class="bg-foreground text-background flex size-8 items-center justify-center rounded-lg font-serif text-lg leading-none font-semibold" aria-hidden="true">{appName.charAt(0)}</span>
      {appName}
    </Link>
    <div class="mb-4 px-1"><SearchBox bind:value={q} /></div>
    <nav aria-label="Main navigation" class="flex flex-col gap-0.5">
      {#each primary as item (item.href)}
        <Link href={item.href} aria-current={isActive(item.href) ? 'page' : undefined} class={linkClass(item.href)}>
          <item.icon class={cn('size-[18px] transition-transform group-active:scale-90', isActive(item.href) && 'text-brand')} strokeWidth={isActive(item.href) ? 2.25 : 1.75} aria-hidden="true" />
          {item.label}
        </Link>
      {/each}
    </nav>
    {#if library.length}
      <nav aria-label="Your library" class="mt-6 flex flex-col gap-0.5">
        <p class="text-muted-foreground px-3 pb-1.5 text-xs font-medium">Library</p>
        {#each library as item (item.href)}
          <Link href={item.href} aria-current={isActive(item.href) ? 'page' : undefined} class={linkClass(item.href)}>
            <item.icon class={cn('size-[18px] transition-transform group-active:scale-90', isActive(item.href) && 'text-brand')} strokeWidth={isActive(item.href) ? 2.25 : 1.75} aria-hidden="true" />
            {item.label}
          </Link>
        {/each}
      </nav>
    {/if}
    {#if user?.verified}
      <Button href="/posts/create" class="mt-6 h-10 rounded-lg text-[15px] transition-transform active:scale-[0.98]">
        <PenLine class="size-4" /> Write a post
      </Button>
    {/if}

    <div class="mt-auto">
      {#if user}
        <DropdownMenu entries={accountEntries} align="start" side="top" contentClass="w-60" triggerClass="hover:bg-accent flex w-full items-center gap-3 rounded-lg p-2 text-left transition-colors" triggerLabel="Account menu">
          {#snippet trigger()}
            <Avatar src={user.avatarUrl} fallback={initials} class="size-8" fallbackClass="text-xs" />
            <span class="min-w-0 flex-1">
              <span class="block truncate text-sm font-medium">{user.name}</span>
              <span class="text-muted-foreground block truncate text-xs">{user.email}</span>
            </span>
            <MoreHorizontal class="text-muted-foreground size-4" aria-hidden="true" />
          {/snippet}
        </DropdownMenu>
      {:else}
        <div class="flex flex-col gap-2 rounded-xl border p-3">
          <p class="text-sm font-medium">Join the conversation</p>
          <p class="text-muted-foreground text-xs leading-5">Follow writers, like posts and build your own feed.</p>
          <Button href="/register" size="sm" class="mt-1 rounded-lg">Create account</Button>
          <Button href="/login" size="sm" variant="ghost" class="rounded-lg">Log in</Button>
        </div>
      {/if}
    </div>
  </aside>

  <!-- Mobile top bar -->
  <header class="bg-background/80 sticky top-0 z-30 border-b backdrop-blur-md lg:hidden">
    <div class="flex h-14 items-center gap-3 px-4">
      {#if searching}
        <div class="flex-1"><SearchBox bind:value={q} /></div>
        <Button variant="ghost" size="icon-sm" aria-label="Close search" onclick={() => (searching = false)}><X /></Button>
      {:else}
        <Link href="/posts" class="flex items-center gap-2 font-semibold tracking-tight">
          <span class="bg-foreground text-background flex size-7 items-center justify-center rounded-lg font-serif text-base leading-none font-semibold" aria-hidden="true">{appName.charAt(0)}</span>
          {appName}
        </Link>
        <Button variant="ghost" size="icon-sm" class="ml-auto rounded-full" aria-label="Search" onclick={() => (searching = true)}><Search /></Button>
        {#if user}
          <DropdownMenu entries={mobileEntries} align="end" contentClass="w-60" triggerLabel="Account menu" triggerClass="rounded-full">
            {#snippet trigger()}
              <Avatar src={user.avatarUrl} fallback={initials} class="size-7" fallbackClass="text-xs" />
            {/snippet}
          </DropdownMenu>
        {:else}
          <Button href="/register" size="sm" class="rounded-full">Sign up</Button>
        {/if}
      {/if}
    </div>
  </header>

  <div class="lg:pl-64">
    {#if user && !user.verified}
      <div class="bg-brand/8 border-b px-4 py-2.5 text-center text-sm">
        Verify your email to post, comment and like.
        <Link href="/email/verify" class="text-brand font-medium underline-offset-4 hover:underline">Resend link</Link>
      </div>
    {/if}
    {#key page.url.split('?')[0]}
      <main class="animate-rise mx-auto w-full max-w-5xl px-4 pt-6 pb-28 sm:px-6 lg:px-10 lg:pt-10 lg:pb-16">
        {@render children?.()}
      </main>
    {/key}
  </div>

  <!-- Mobile tab bar -->
  <nav aria-label="Tabs" class="bg-background/90 fixed inset-x-0 bottom-0 z-30 border-t pb-[env(safe-area-inset-bottom)] backdrop-blur-md lg:hidden">
    <div class="mx-auto flex max-w-md items-stretch justify-around">
      {#each tabs as tab (tab.href)}
        <Link
          href={tab.href} aria-current={isActive(tab.href) ? 'page' : undefined}
          class={cn('text-muted-foreground flex flex-1 flex-col items-center gap-1 py-2 text-[11px] transition-colors active:scale-95', isActive(tab.href) && 'text-foreground')}
        >
          {#if tab.href === '/posts/create'}
            <span class="bg-foreground text-background flex size-8 items-center justify-center rounded-full">
              <tab.icon class="size-4" aria-hidden="true" />
            </span>
          {:else}
            <tab.icon class={cn('size-5', isActive(tab.href) && 'text-brand')} strokeWidth={isActive(tab.href) ? 2.25 : 1.75} aria-hidden="true" />
          {/if}
          <span class={cn(tab.href === '/posts/create' && 'sr-only')}>{tab.label}</span>
        </Link>
      {/each}
    </div>
  </nav>

  {#if browser}<Toaster position="bottom-right" closeButton />{/if}
</div>
