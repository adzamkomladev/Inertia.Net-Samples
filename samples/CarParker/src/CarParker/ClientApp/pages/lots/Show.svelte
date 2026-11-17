<script lang="ts">
  import { Deferred, Link } from '@inertiajs/svelte'
  import ArrowLeft from '@lucide/svelte/icons/arrow-left'
  import CalendarRange from '@lucide/svelte/icons/calendar-range'
  import MapPin from '@lucide/svelte/icons/map-pin'
  import Navigation from '@lucide/svelte/icons/navigation'
  import Phone from '@lucide/svelte/icons/phone'
  import ShieldCheck from '@lucide/svelte/icons/shield-check'
  import Availability from '@/components/Availability.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import SpotGrid from '@/components/SpotGrid.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Skeleton from '@/components/ui/skeleton/Skeleton.svelte'
  import { money } from '@/lib/money'
  import { zoneLabel } from '@/lib/time'
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
  const bookHref = $derived(`/lots/${lot.id}/book`)
  // Calendar dates are the lot's own days (yyyy-MM-dd), so they format as plain dates.
  const dayLabel = (date: string, index: number) =>
    index === 0 ? 'Today' : index === 1 ? 'Tomorrow'
      : new Date(`${date}T00:00:00Z`).toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short', timeZone: 'UTC' }).replace(',', '')
</script>

<PageTitle title={lot.name} description={`Parking in ${lot.area}: live spot map, hourly rate and weekly bookings.`} />

<div class="mx-auto w-full max-w-6xl px-4 pt-6 pb-[calc(6rem+env(safe-area-inset-bottom))] sm:px-6 lg:pb-12">
  <Link href="/lots" class="text-muted-foreground hover:text-foreground -ml-1 inline-flex min-h-11 items-center gap-1.5 rounded-lg px-1 text-sm font-medium">
    <ArrowLeft class="size-4" aria-hidden="true" /> All lots
  </Link>

  <header class="mt-1 grid gap-6 md:grid-cols-[1fr_20rem] md:items-end">
    <div class="min-w-0">
      <p class="text-muted-foreground text-sm font-medium">{lot.area}</p>
      <h1 class="mt-1 text-4xl font-bold tracking-tight text-balance">{lot.name}</h1><p class="mt-2 text-sm text-muted-foreground">{lot.open24Hours ? 'Open 24 hours' : `Open daily ${lot.opens}–${lot.closes}`} · times are {zoneLabel(lot.timeZoneId)}</p>
      <p class="text-muted-foreground mt-3 flex flex-wrap items-center gap-x-5 gap-y-1">
        <span class="inline-flex items-center gap-1.5"><MapPin class="size-4" aria-hidden="true" /> {lot.address}</span>
        {#if lot.contactPhone}<a href={`tel:${lot.contactPhone.replace(/[^0-9+]/g, '')}`} class="hover:text-foreground inline-flex min-h-11 items-center gap-1.5 underline-offset-4 hover:underline md:min-h-0"><Phone class="size-4" aria-hidden="true" /> {lot.contactPhone}</a>{/if}
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
          <Button href={bookHref} prefetch="hover" cacheFor="30s" disabled={!freeSpots}>
            <CalendarRange aria-hidden="true" /> Choose a bay and book
          </Button>
        </div>
        <SpotGrid {spots} class="mt-5" />
      </section>

      <section class="bg-card shadow-card rounded-xl p-4 sm:p-6" aria-labelledby="days-title">
        <h2 id="days-title" class="text-xl font-bold">Next 14 days</h2>
        <p class="text-muted-foreground text-sm">Free bays expected each day.</p>
        <Deferred data="calendar">
          {#snippet fallback()}
            <Skeleton class="mt-4 h-[8.25rem] w-full" />
          {/snippet}
          {#snippet rescue()}
            <p class="text-muted-foreground bg-muted mt-4 flex min-h-[8.25rem] items-center justify-center rounded-lg px-4 text-center text-sm" role="alert">Availability for the next 14 days could not load. Reload the page to try again.</p>
          {/snippet}
          {#if !calendar?.length}
            <p class="text-muted-foreground bg-muted mt-4 flex min-h-[8.25rem] items-center justify-center rounded-lg px-4 text-center text-sm">No dates are open for booking at this lot yet. Check back soon.</p>
          {:else}
          <ol class="mt-3 grid grid-cols-2 gap-x-5 sm:gap-x-8">
            {#each calendar ?? [] as day, index (day.date)}
              <li class="flex min-w-0 flex-col gap-1 border-b py-2">
                <span class="flex items-baseline justify-between gap-2 text-sm">
                  <span class="truncate font-medium">{dayLabel(day.date, index)}</span>
                  <span class="shrink-0 tabular-nums"><span class="font-semibold">{day.free}</span><span class="text-muted-foreground"> free</span></span>
                </span>
                <span class="bg-muted block h-1.5 overflow-hidden rounded-full" aria-hidden="true">
                  <span class="bg-spot-free block h-full rounded-full" style={`width:${day.total ? Math.round((day.free / day.total) * 100) : 0}%`}></span>
                </span>
              </li>
            {/each}
          </ol>
          {/if}
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
        <Button href={bookHref} prefetch="hover" cacheFor="30s" size="lg" class="mt-4 w-full" disabled={!freeSpots}>
          {freeSpots ? 'Book a bay' : 'Lot is full'}
        </Button>
      </section>
      {#if lot.arrivalNotes}
        <section class="bg-primary/5 ring-primary/15 rounded-xl p-5 ring-1 ring-inset" aria-labelledby="arrival-title">
          <h2 id="arrival-title" class="flex items-center gap-2 text-base font-bold"><Navigation class="text-primary size-5" aria-hidden="true" /> Getting in</h2>
          <p class="text-muted-foreground mt-2 text-sm leading-6">{lot.arrivalNotes}</p>
        </section>
      {/if}
      <section class="bg-card shadow-card rounded-xl p-5" aria-labelledby="cover-title">
        <h2 id="cover-title" class="flex items-center gap-2 text-base font-bold"><ShieldCheck class="text-primary size-5" aria-hidden="true" /> Spot Cover</h2>
        <p class="text-muted-foreground mt-2 text-sm leading-6">
          Optional at checkout. {tariff.insurancePercent}% of the parking charge (minimum {money(tariff.insuranceMinCents)}) covers damage or theft up to {money(tariff.coverageCents)}.
        </p>
      </section>
    </aside>
  </div>
</div>

<!-- Phones: rate, live free bays and the booking button stay in reach. -->
<div class="bg-card/95 pb-safe fixed inset-x-0 bottom-0 z-30 shadow-pop backdrop-blur lg:hidden">
  <div class="mx-auto flex max-w-6xl items-center gap-3 px-4 py-3">
    <div class="min-w-0 flex-1" aria-live="polite">
      <p class="font-display text-2xl leading-none font-bold tabular-nums">{money(tariff.ratePerHourCents)}<span class="text-muted-foreground text-sm font-medium"> / hour</span></p>
      <p class="text-muted-foreground mt-1 truncate text-xs">{freeSpots ? `${freeSpots} of ${lot.spots} bays free now · max ${money(tariff.dailyMaxCents)}/day` : 'Every bay is taken right now'}</p>
    </div>
    <Button href={bookHref} prefetch="hover" cacheFor="30s" size="lg" class="min-h-12" disabled={!freeSpots}>{freeSpots ? 'Book a bay' : 'Lot is full'}</Button>
  </div>
</div>
