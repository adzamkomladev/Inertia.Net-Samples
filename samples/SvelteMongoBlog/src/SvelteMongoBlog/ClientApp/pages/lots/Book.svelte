<script lang="ts">
  import { ArrowLeft, CarFront } from '@lucide/svelte'
  import SpotGrid from '@/components/SpotGrid.svelte'
  import WindowPicker from '@/components/WindowPicker.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import { money } from '@/lib/money'
  import type { LotCard, SpotView, Tariff } from '@/types'

  /** GET /lots/{id}/book — booking wizard: pick a spot on the live grid, configure the window, submit. */
  let {
    lot,
    tariff,
    spots,
    prefillSpot,
  }: {
    lot: LotCard
    tariff: Tariff
    spots: SpotView[]
    prefillSpot: string | null
  } = $props()

  let selected = $state<SpotView | undefined>(
    (prefillSpot && (spots.find(s => s.id === prefillSpot && s.state === 'free') ?? undefined)) || undefined,
  )

  /** Clicking a free bay selects it; the wizard's WindowPicker owns the booking form. */
  const onselect = (spot: SpotView) => {
    if (spot.state !== 'free') return
    selected = spot
  }

  const freeCount = $derived(spots.filter(s => s.state === 'free').length)
</script>

<PageTitle title={`Book · ${lot.name}`} />

<div class="mx-auto w-full max-w-6xl">
  <Button href={`/lots/${lot.id}`} variant="ghost" size="sm" class="-ml-2 rounded-xl">
    <ArrowLeft class="size-4" aria-hidden="true" /> Back to {lot.name}
  </Button>

  <header class="mt-3 flex flex-wrap items-end justify-between gap-3">
    <div>
      <h1 class="font-display text-2xl font-semibold tracking-tight">Book a window at {lot.name}</h1>
      <p class="text-muted-foreground mt-1 text-[15px]">
        {money(tariff.ratePerHourCents)}/hr · daily max {money(tariff.dailyMaxCents)} ·
        <span class="font-mono tabular-nums">{freeCount}</span> spots free
      </p>
    </div>
  </header>

  <div class="mt-6 grid items-start gap-6 lg:grid-cols-[1fr_26rem]">
    <!-- Spot map -->
    <section class="bg-card shadow-card rounded-3xl border p-5 sm:p-6">
      <div class="flex items-center justify-between gap-3">
        <h2 class="font-display text-lg font-semibold">Pick your spot</h2>
        {#if selected}
          <span class="bg-spot-free/15 text-spot-free-foreground inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium">
            <CarFront class="size-3.5" aria-hidden="true" /> {selected.label} · level {selected.level}
          </span>
        {:else}
          <span class="text-muted-foreground text-xs">Tap a mint tile</span>
        {/if}
      </div>
      <SpotGrid
        spots={spots}
        selectable
        selectedId={selected?.id}
        onselect={onselect}
        class="mt-5"
      />
      {#if !freeCount}
        <p class="text-destructive mt-4 text-sm" role="alert">This deck has no free spots right now — check back soon.</p>
      {/if}
    </section>

    <!-- Wizard -->
    <section class="bg-card shadow-card rounded-3xl border p-5 sm:p-6">
      <h2 class="font-display text-lg font-semibold">Your window</h2>
      <WindowPicker {lot} {tariff} selectedSpot={selected ?? null} class="mt-5" />
    </section>
  </div>
</div>
