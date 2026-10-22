<script lang="ts">
  import type { Snippet } from 'svelte'
  import { Link, router, usePage } from '@inertiajs/svelte'
  import { Home, RadioTower, ScrollText } from '@lucide/svelte'
  import { Toaster } from 'svelte-sonner'
  import Select from '@/components/ui/select/Select.svelte'
  import ThemeToggle from '@/components/ThemeToggle.svelte'
  import { useFlashToast } from '@/lib/flash-toast.svelte'
  import { useRealtime } from '@/lib/realtime.svelte'
  import { useTheme } from '@/lib/theme.svelte'
  import { cn } from '@/lib/utils'
  import type { LotOption } from '@/types'

  /**
   * Gate-console shell for a tablet: a high-contrast ink bar with the active-lot picker and
   * three large tabs. BoardChanged events refresh the page through useRealtime.
   */
  let { children }: { children?: Snippet } = $props()

  const browser = typeof window !== 'undefined'
  const page = usePage()

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
    { href: '/guard', label: 'Gate', match: (url: string) => url.split('?')[0] === '/guard' },
    { href: '/guard/board', label: 'Board', match: (url: string) => url.startsWith('/guard/board') },
    { href: '/guard/incidents', label: 'Incidents', match: (url: string) => url.startsWith('/guard/incidents'), icon: ScrollText },
  ]
</script>

<a href="#main" class="bg-primary text-primary-foreground sr-only z-50 rounded-lg px-4 py-2 focus:not-sr-only focus:fixed focus:top-3 focus:left-3">Skip to content</a>

<div class="bg-background flex min-h-screen flex-col">
  <header class="bg-foreground text-background sticky top-0 z-40">
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
          class="ring-background/30 bg-background/10 text-background hover:bg-background/15 h-12 min-w-48 text-base font-semibold md:h-12 [&_svg]:text-background/70"
        />
      {/if}

      <nav class="ml-auto flex items-center gap-1" aria-label="Console">
        {#each tabs as tab (tab.href)}
          <Link
            href={tab.href}
            aria-current={tab.match(page.url) ? 'page' : undefined}
            class={cn(
              'flex min-h-12 items-center rounded-lg px-4 text-base font-semibold transition-colors',
              tab.match(page.url) ? 'bg-background text-foreground' : 'text-background/80 hover:bg-background/10',
            )}
          >{tab.label}</Link>
        {/each}
        <ThemeToggle class="text-background/80 hover:text-background hover:bg-background/10 size-12" />
        <a href="/" class="text-background/80 hover:bg-background/10 inline-flex size-12 items-center justify-center rounded-lg" aria-label="Public site">
          <Home class="size-5" aria-hidden="true" />
        </a>
      </nav>
    </div>
  </header>

  <main id="main" class="mx-auto w-full max-w-7xl flex-1 px-4 py-6 pb-20 sm:px-6 lg:py-8">
    {@render children?.()}
  </main>

  {#if browser}<Toaster position="bottom-right" closeButton />{/if}
</div>
