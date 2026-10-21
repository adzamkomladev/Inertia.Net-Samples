<script lang="ts">
  import { CalendarRange, MapPin, Phone, ShieldCheck } from '@lucide/svelte'
  import OccupancyRing from '@/components/OccupancyRing.svelte'
  import PriceTag from '@/components/PriceTag.svelte'
  import SpotGrid from '@/components/SpotGrid.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import Skeleton from '@/components/ui/skeleton/Skeleton.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import { money } from '@/lib/money'
  import type { DayAvailability, LotCard, SpotView, Tariff } from '@/types'

  /** GET /lots/{id} — live spot grid + tariff card + 14-day availability strip. Public and SSR'd. */
  let {
    lot,
    spots,
    tariff,
    calendar,
  }: {
    lot: LotCard
    spots: SpotView[]
    tariff: Tariff
    calendar?: DayAvailability[]
  } = $props()

  const freeSpots = $derived(spots.filter(s => s.state === 'free').length)
  const bookedPct = $derived(lot.spots ? Math.round(((lot.spots - lot.free) * 100) / lot.spots) : 0)

  const canBook = $derived(freeSpots > 0)
  const maxFree = $derived(Math.max(1, ...(calendar ?? []).map(d => d.free)))
</script>

<PageTitle title={lot.name} description={`Premium parking in ${lot.area} — live spot map, hourly rates and weekly windows.`} />

<div class="mx-auto w-full max-w-6xl">
  <!-- Header band -->
  <section class="bg-primary text-primary-foreground shadow-lifted relative overflow-hidden rounded-3xl">
    <div class="deck-grid absolute inset-0 opacity-40" aria-hidden="true"></div>
    <div class="relative flex flex-col gap-6 p-6 sm:flex-row sm:items-center sm:justify-between sm:p-8">
      <div class="min-w-0">
        <p class="text-cta text-[11px] font-semibold tracking-[0.18em] uppercase">{lot.area}</p>
        <h1 class="font-display mt-1 truncate text-3xl font-semibold tracking-tight sm:text-4xl">{lot.name}</h1>
        <p class="text-primary-foreground/70 mt-2 flex items-center gap-1.5 text-sm">
          <MapPin class="size-4 shrink-0" aria-hidden="true" /> {lot.address}
        </p>
        {#if lot.contactPhone}
          <p class="text-primary-foreground/70 mt-1 flex items-center gap-1.5 text-sm">
            <Phone class="size-4 shrink-0" aria-hidden="true" /> {lot.contactPhone}
          </p>
        {/if}
      </div>
      <div class="flex shrink-0 items-center gap-5">
        <div class="bg-white/10 rounded-2xl p-3 backdrop-blur">
          <OccupancyRing value={bookedPct} size={92} label="full" />
        </div>
        <div class="text-sm">
          <p class="font-mono text-2xl font-semibold tabular-nums">
            {freeSpots}<span class="text-primary-foreground/50 text-base">/{lot.spots}</span>
          </p>
          <p class="text-primary-foreground/60 text-xs">spots free now</p>
          <p class="text-primary-foreground/60 mt-2 text-xs">
            {money(tariff.ratePerHourCents)}/hr · max {money(tariff.dailyMaxCents)}/day
          </p>
        </div>
      </div>
    </div>
  </section>

  <!-- Availability strip (deferred) -->
  {#if calendar}
    <section class="mt-6">
      <div class="bg-card shadow-card rounded-2xl border p-4">
        <div class="flex items-center justify-between">
          <h2 class="text-sm font-semibold">Next 14 days</h2>
          <p class="text-muted-foreground text-xs">free spots per day</p>
        </div>
        <div class="scrollbar-none mt-3 flex gap-1.5 overflow-x-auto pb-1">
          {#each calendar as day (day.date)}
            {@const height = Math.round((day.free / maxFree) * 100)}
            <div class="flex w-9 shrink-0 flex-col items-center gap-1">
              <div class="bg-muted/60 flex h-16 w-full items-end overflow-hidden rounded-md">
                <div class="bg-spot-free w-full rounded-md transition-all duration-500 ease-glide" style={`height:${height}%`}></div>
              </div>
              <span class="text-muted-foreground font-mono text-[10px] tabular-nums">{day.date.slice(8)}</span>
            </div>
          {/each}
        </div>
      </div>
    </section>
  {:else}
    <Skeleton class="mt-6 h-36 rounded-2xl" />
  {/if}

  <div class="mt-6 grid gap-6 lg:grid-cols-[1fr_20rem]">
    <!-- Live spot map -->
    <section class="bg-card shadow-card rounded-3xl border p-5 sm:p-6">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 class="font-display text-lg font-semibold">Live spot map</h2>
          <p class="text-muted-foreground text-[13px]">Tiles flip in real time as the deck fills and empties.</p>
        </div>
        {#if canBook}
          <Button href={`/lots/${lot.id}/book`} class="rounded-2xl">
            <CalendarRange class="size-4" aria-hidden="true" /> Book a window
          </Button>
        {:else}
          <Button href={`/lots/${lot.id}/book`} variant="outline" class="rounded-2xl">Join waitlist</Button>
        {/if}
      </div>
      <SpotGrid {spots} class="mt-5" />
    </section>

    <!-- Tariff card -->
    <aside class="flex h-fit flex-col gap-4 lg:sticky lg:top-24">
      <div class="bg-card shadow-card rounded-3xl border p-5">
        <h2 class="font-display text-base font-semibold">Tariff</h2>
        <dl class="mt-4 space-y-3 text-sm">
          <div class="flex items-baseline justify-between gap-3">
            <dt class="text-muted-foreground">Per hour</dt>
            <dd><PriceTag cents={tariff.ratePerHourCents} size="lg" /></dd>
          </div>
          <div class="flex items-baseline justify-between gap-3">
            <dt class="text-muted-foreground">Daily max</dt>
            <dd><PriceTag cents={tariff.dailyMaxCents} size="lg" /></dd>
          </div>
          <div class="flex items-baseline justify-between gap-3">
            <dt class="text-muted-foreground">Spot Cover</dt>
            <dd class="text-right">
              <span class="font-medium">{tariff.insurancePercent}%</span>
              <span class="text-muted-foreground block text-[11px]">min {money(tariff.insuranceMinCents)} · up to {money(tariff.coverageCents)}</span>
            </dd>
          </div>
        </dl>
        <Button href={`/lots/${lot.id}/book`} size="lg" class="mt-5 w-full rounded-2xl" disabled={!canBook}>
          {canBook ? 'Reserve now' : 'Deck is full'}
        </Button>
      </div>
      <div class="bg-spot-booked/8 border-spot-booked/25 rounded-2xl border p-4">
        <p class="flex items-center gap-2 text-sm font-medium">
          <ShieldCheck class="text-spot-booked size-4" aria-hidden="true" /> Covered deck
        </p>
        <p class="text-muted-foreground mt-1 text-[13px] leading-5">
          Add Spot Cover at checkout: {tariff.insurancePercent}% of parking (min {money(tariff.insuranceMinCents)}) covers damage or theft up to {money(tariff.coverageCents)}.
        </p>
      </div>
    </aside>
  </div>
</div>
