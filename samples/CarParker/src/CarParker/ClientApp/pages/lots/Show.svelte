<script lang="ts">
  import { Deferred } from '@inertiajs/svelte'
  import { ArrowLeft, CalendarRange, MapPin, Phone, ShieldCheck } from '@lucide/svelte'
  import Availability from '@/components/Availability.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import SpotGrid from '@/components/SpotGrid.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Skeleton from '@/components/ui/skeleton/Skeleton.svelte'
  import { money } from '@/lib/money'
  import type { DayAvailability, LotCard, SpotView, Tariff } from '@/types'

  /** GET /lots/{id}: live floor plan, pricing explained, two weeks of availability. Public and SSR'd. */
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
  const maxFree = $derived(Math.max(1, ...(calendar ?? []).map(d => d.free)))
  const bookHref = $derived(`/lots/${lot.id}/book`)
  const weekday = (date: string) => new Date(`${date}T00:00:00Z`).toLocaleDateString('en-US', { weekday: 'short', timeZone: 'UTC' })
</script>

<PageTitle title={lot.name} description={`Parking in ${lot.area}: live spot map, hourly rate and weekly bookings.`} />

<div class="mx-auto w-full max-w-6xl px-4 pt-6 sm:px-6">
  <a href="/lots" class="text-muted-foreground hover:text-foreground -ml-1 inline-flex min-h-11 items-center gap-1.5 rounded-lg px-1 text-sm font-medium">
    <ArrowLeft class="size-4" aria-hidden="true" /> All lots
  </a>

  <header class="mt-1 grid gap-6 md:grid-cols-[1fr_20rem] md:items-end">
    <div class="min-w-0">
      <p class="text-muted-foreground text-xs font-semibold tracking-wider uppercase">{lot.area}</p>
      <h1 class="mt-1 text-4xl font-bold tracking-tight text-balance">{lot.name}</h1>
      <p class="text-muted-foreground mt-3 flex flex-wrap items-center gap-x-5 gap-y-1">
        <span class="inline-flex items-center gap-1.5"><MapPin class="size-4" aria-hidden="true" /> {lot.address}</span>
        {#if lot.contactPhone}<span class="inline-flex items-center gap-1.5"><Phone class="size-4" aria-hidden="true" /> {lot.contactPhone}</span>{/if}
      </p>
    </div>
    <div class="bg-card shadow-card rounded-xl p-4">
      <Availability free={freeSpots} total={lot.spots} />
    </div>
  </header>

  <div class="mt-8 grid items-start gap-6 lg:grid-cols-[1fr_20rem]">
    <div class="flex min-w-0 flex-col gap-6">
      <section class="bg-card shadow-card rounded-xl p-4 sm:p-6" aria-labelledby="map-title">
        <div class="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 id="map-title" class="text-xl font-bold">Spot map</h2>
            <p class="text-muted-foreground text-sm">Updates live as cars arrive and leave.</p>
          </div>
          <Button href={bookHref} disabled={!freeSpots}>
            <CalendarRange aria-hidden="true" /> Choose a bay and book
          </Button>
        </div>
        <SpotGrid {spots} class="mt-5" />
      </section>

      <section class="bg-card shadow-card rounded-xl p-4 sm:p-6" aria-labelledby="days-title">
        <h2 id="days-title" class="text-xl font-bold">Next 14 days</h2>
        <p class="text-muted-foreground text-sm">Free bays at the start of each day.</p>
        <Deferred data="calendar">
          {#snippet fallback()}
            <Skeleton class="mt-4 h-28 w-full" />
          {/snippet}
          <ol class="scrollbar-none mt-4 flex gap-1.5 overflow-x-auto pb-1">
            {#each calendar ?? [] as day (day.date)}
              <li class="flex w-11 shrink-0 flex-col items-center gap-1">
                <span class="text-sm font-semibold tabular-nums">{day.free}</span>
                <div class="bg-muted flex h-16 w-full items-end overflow-hidden rounded-md" aria-hidden="true">
                  <div class="bg-spot-free w-full" style={`height:${Math.round((day.free / maxFree) * 100)}%`}></div>
                </div>
                <span class="text-muted-foreground text-xs">{weekday(day.date)}</span>
                <span class="text-xs font-semibold tabular-nums">{day.date.slice(8)}</span>
              </li>
            {/each}
          </ol>
        </Deferred>
      </section>
    </div>

    <aside class="flex flex-col gap-4 lg:sticky lg:top-24">
      <section class="bg-card shadow-card rounded-xl p-5" aria-labelledby="price-title">
        <h2 id="price-title" class="text-xl font-bold">Pricing</h2>
        <p class="font-display mt-3 text-4xl font-bold tabular-nums">{money(tariff.ratePerHourCents)}<span class="text-muted-foreground text-base font-medium"> / hour</span></p>
        <p class="text-muted-foreground mt-2 text-sm leading-6">
          Charged per started hour. You never pay more than <strong class="text-foreground">{money(tariff.dailyMaxCents)}</strong> in any 24 hours.
        </p>
        <Button href={bookHref} size="lg" class="mt-4 w-full" disabled={!freeSpots}>
          {freeSpots ? 'Book a bay' : 'Lot is full'}
        </Button>
      </section>
      <section class="bg-card shadow-card rounded-xl p-5" aria-labelledby="cover-title">
        <h2 id="cover-title" class="flex items-center gap-2 text-base font-bold"><ShieldCheck class="text-primary size-5" aria-hidden="true" /> Spot Cover</h2>
        <p class="text-muted-foreground mt-2 text-sm leading-6">
          Optional at checkout. {tariff.insurancePercent}% of the parking charge (minimum {money(tariff.insuranceMinCents)}) covers damage or theft up to {money(tariff.coverageCents)}.
        </p>
      </section>
    </aside>
  </div>
</div>
