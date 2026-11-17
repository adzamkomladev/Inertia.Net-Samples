<script lang="ts">
  import type { Snippet } from 'svelte'
  import { Link, router, usePage } from '@inertiajs/svelte'
  import CarFront from '@lucide/svelte/icons/car-front'
  import Home from '@lucide/svelte/icons/house'
  import RadioTower from '@lucide/svelte/icons/radio-tower'
  import ScrollText from '@lucide/svelte/icons/scroll-text'
  import Select from '@/components/ui/select/Select.svelte'
  import NavigationSkeleton from '@/components/NavigationSkeleton.svelte'
  import NotificationBell from '@/components/NotificationBell.svelte'
  import ThemeToggle from '@/components/ThemeToggle.svelte'
  import { usePendingVisit } from '@/lib/pending-visit.svelte'
  import { useFlashToast } from '@/lib/flash-toast.svelte'
  import { useRealtime } from '@/lib/realtime.svelte'
  import { useTheme } from '@/lib/theme.svelte'
  import { cn } from '@/lib/utils'
  import type { LotOption } from '@/types'

  /**
   * Gate-console shell for a tablet or phone: a high-contrast ink bar with the active-lot picker
   * (full width on phones) and two large tabs (a bottom bar on phones). Board events refresh the
   * page through useRealtime.
   */
  let { children }: { children?: Snippet } = $props()

  const page = usePage()
  const pending = usePendingVisit()

  useTheme()
  useRealtime(() => 'guard')
  useFlashToast()

  const lots = $derived((page.props as { lots?: LotOption[] }).lots ?? [])
  const lotId = $derived((page.props as { lotId?: string | null }).lotId ?? '')
  const items = $derived(lots.map(lot => ({ value: lot.id, label: lot.name })))

  const onLotChange = (value: string) => {
    if (value && value !== lotId) router.get('/guard', { lot: value }, { preserveScroll: true })
  }

  const tabs = [
    { href: '/guard', label: 'Gate', icon: CarFront, match: (url: string) => url.split('?')[0] === '/guard' },
    { href: '/guard/incidents', label: 'Incidents', icon: ScrollText, match: (url: string) => url.startsWith('/guard/incidents') },
  ]
</script>

<a href="#main" class="bg-primary text-primary-foreground sr-only z-50 rounded-lg px-4 py-2 focus:not-sr-only focus:fixed focus:top-3 focus:left-3">Skip to content</a>

<div class="bg-background flex min-h-screen flex-col">
  <header class="bg-slate-950 text-slate-100 pt-safe sticky top-0 z-40">
    <div class="mx-auto flex w-full max-w-7xl flex-wrap items-center gap-x-4 gap-y-2 px-4 py-2.5 sm:px-6">
      <p class="font-display flex items-center gap-2 text-lg font-bold tracking-tight">
        <RadioTower class="text-spot-free size-5" aria-hidden="true" /> Gate
      </p>

      {#if lotId && items.length}
        <Select
          items={items}
          value={lotId}
          onValueChange={onLotChange}
          ariaLabel="Active lot"
          class="ring-white/30 bg-white/10 text-slate-100 hover:bg-white/15 order-last h-12 w-full text-base font-semibold sm:order-none sm:w-auto sm:min-w-48 [&_svg]:text-slate-300"
        />
      {/if}

      <nav class="ml-auto flex items-center gap-0.5" aria-label="Console">
        {#each tabs as tab (tab.href)}
          <Link
            href={tab.href}
            prefetch="hover"
            cacheFor="30s"
            aria-current={tab.match(page.url) ? 'page' : undefined}
            class={cn(
              'hidden min-h-12 items-center rounded-lg px-4 text-base font-semibold transition-colors sm:flex',
              tab.match(page.url) ? 'bg-background text-foreground' : 'text-slate-300 hover:bg-white/10',
            )}
          >{tab.label}</Link>
        {/each}
        <NotificationBell /><ThemeToggle class="text-slate-300 hover:text-slate-100 hover:bg-white/10 size-12" />
        <a href="/" class="text-slate-300 hover:bg-white/10 inline-flex size-12 items-center justify-center rounded-lg" aria-label="Public site">
          <Home class="size-5" aria-hidden="true" />
        </a>
      </nav>
    </div>
  </header>

  <main id="main" class="mx-auto w-full max-w-7xl flex-1 px-4 py-6 pb-[calc(5.5rem+env(safe-area-inset-bottom))] sm:px-6 sm:pb-12 lg:py-8">
    {#if pending.current}<NavigationSkeleton kind={pending.current.kind} />{/if}
    <div class={pending.current ? 'hidden' : 'contents'}>{@render children?.()}</div>
  </main>

  <!-- Phones: console tabs at the thumb, 56px tall. -->
  <nav class="bg-slate-950 pb-safe fixed inset-x-0 bottom-0 z-40 border-t border-white/10 sm:hidden" aria-label="Console quick navigation">
    <ul class="grid grid-cols-2">
      {#each tabs as tab (tab.href)}
        <li>
          <Link href={tab.href} prefetch="hover" cacheFor="30s" aria-current={tab.match(page.url) ? 'page' : undefined} class={cn('flex min-h-14 flex-col items-center justify-center gap-1 text-xs font-semibold', tab.match(page.url) ? 'text-white' : 'text-slate-400')}>
            <tab.icon class="size-5" aria-hidden="true" />{tab.label}
          </Link>
        </li>
      {/each}
    </ul>
  </nav>
</div>
