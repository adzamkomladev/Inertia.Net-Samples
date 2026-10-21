<script lang="ts">
  import type { Snippet } from 'svelte'
  import { router, usePage } from '@inertiajs/svelte'
  import { RadioTower, ScrollText, ShieldUser } from '@lucide/svelte'
  import { Toaster } from 'svelte-sonner'
  import Select from '@/components/ui/select/Select.svelte'
  import ThemeToggle from '@/components/ThemeToggle.svelte'
  import { useFlashToast } from '@/lib/flash-toast.svelte'
  import { useRealtime } from '@/lib/realtime.svelte'
  import { useTheme } from '@/lib/theme.svelte'
  import { cn } from '@/lib/utils'
  import type { LotOption } from '@/types'

  /**
   * Guard console shell: one dark ops bar with the live lot picker, board/incidents links
   * and a `working` snippet for the page's big action buttons. BoardChanged events refresh
   * the active console page through useRealtime.
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

  const consoleHref = '/guard'
  const board = '/guard/board'
  const incidents = '/guard/incidents'
  const active = (href: string) => page.url === href || page.url.startsWith(`${href}?`)
</script>

<div class="bg-background flex min-h-screen flex-col">
  <!-- Ops bar -->
  <header class="bg-primary text-primary-foreground sticky top-0 z-40 shadow-lifted">
    <div class="mx-auto flex h-16 w-full max-w-7xl flex-wrap items-center gap-3 px-4 sm:px-6">
      <div class="text-primary-foreground/90 [&_span:last-child]:text-primary-foreground/90">
        <a href="/guard" data-inertia-link class="flex items-center gap-2.5">
          <span class="bg-cta text-cta-foreground flex size-8 items-center justify-center rounded-xl font-display text-lg font-bold" aria-hidden="true">P</span>
          <span class="min-w-0">
            <span class="font-display block text-sm leading-tight font-semibold">Guard console</span>
            <span class="text-primary-foreground/60 block text-[11px] leading-tight">{(page.props as any).appName ?? 'CarParker'}</span>
          </span>
        </a>
      </div>

      {#if lotId && items.length}
        <div class="flex min-w-0 items-center gap-2">
          <RadioTower class="text-cta size-4 shrink-0" aria-hidden="true" />
          <Select
            items={items}
            value={lotId}
            onValueChange={onLotChange}
            ariaLabel="Active lot"
            class="border-primary-foreground/20 bg-primary-foreground/10 text-primary-foreground hover:bg-primary-foreground/15 h-9 min-w-40 [&_svg]:text-primary-foreground/70 [&_span[data-slot=select-trigger]]:text-primary-foreground"
          />
        </div>
      {/if}

      <nav class="ml-auto flex items-center gap-1" aria-label="Console">
        <a
          href={consoleHref}
          aria-current={active(consoleHref) && page.url === '/guard' ? 'page' : undefined}
          class={cn(
            'text-primary-foreground/70 hover:text-primary-foreground rounded-xl px-3 py-2 text-sm transition-colors',
            page.url === '/guard' && 'bg-primary-foreground/15 text-primary-foreground font-medium',
          )}
        >Console</a>
        <a
          href={board}
          class={cn(
            'text-primary-foreground/70 hover:text-primary-foreground rounded-xl px-3 py-2 text-sm transition-colors',
            page.url === '/guard/board' && 'bg-primary-foreground/15 text-primary-foreground font-medium',
          )}
        >Board</a>
        <a
          href={incidents}
          class={cn(
            'text-primary-foreground/70 hover:text-primary-foreground rounded-xl px-3 py-2 text-sm transition-colors',
            active(incidents) && 'bg-primary-foreground/15 text-primary-foreground font-medium',
          )}
        ><ScrollText class="mr-1.5 inline size-4" aria-hidden="true" />Incidents</a>
        <span class="mx-1 h-5 w-px bg-primary-foreground/20" aria-hidden="true"></span>
        <ThemeToggle class="text-primary-foreground/70 hover:text-primary-foreground hover:bg-primary-foreground/10" />
        <a href="/" class="text-primary-foreground/70 hover:text-primary-foreground rounded-xl px-3 py-2 text-sm" aria-label="Public site">
          <ShieldUser class="size-4" aria-hidden="true" />
        </a>
      </nav>
    </div>
  </header>

  <main class="animate-rise mx-auto w-full max-w-7xl flex-1 px-4 py-6 pb-24 sm:px-6 lg:py-8 lg:pb-16">
    {@render children?.()}
  </main>

  {#if browser}<Toaster position="bottom-right" closeButton />{/if}
</div>
