<script lang="ts">
  import { Link } from '@inertiajs/svelte'
  import ArrowLeft from '@lucide/svelte/icons/arrow-left'
  import BookingFlow from '@/components/BookingFlow.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import { money } from '@/lib/money'
  import { zoneLabel } from '@/lib/time'
  import type { LotCard, SpotView, Tariff, Vehicle } from '@/types'

  /** GET /lots/{id}/book: when, bay, cover, pay, beside a sticky price summary. */
  let {
    lot,
    tariff,
    spots,
    spotsWindow = null,
    prefillSpot,
    vehicles = [],
  }: {
    lot: LotCard
    tariff: Tariff
    spots: SpotView[]
    /** Set when `spots` shows availability for a chosen window (see BookingFlow). */
    spotsWindow?: string | null
    prefillSpot: string | null
    vehicles?: Vehicle[]
  } = $props()
</script>

<PageTitle title={`Book · ${lot.name}`} />

<div class="mx-auto w-full max-w-6xl px-4 pt-6 pb-[calc(8rem+env(safe-area-inset-bottom))] sm:px-6 lg:pb-12">
  <Link href={`/lots/${lot.id}`} class="text-muted-foreground hover:text-foreground -ml-1 inline-flex min-h-11 items-center gap-1.5 rounded-lg px-1 text-sm font-medium">
    <ArrowLeft class="size-4" aria-hidden="true" /> {lot.name}
  </Link>
  <header class="mt-1 mb-6">
    <h1 class="text-3xl font-bold tracking-tight">Reserve a bay</h1>
    <p class="mt-2 text-sm text-muted-foreground">{lot.open24Hours ? 'Open 24 hours' : `Open daily ${lot.opens}–${lot.closes} ${zoneLabel(lot.timeZoneId)}`}</p>
    <p class="text-muted-foreground mt-1">
      {lot.name}, {lot.address} · {money(tariff.ratePerHourCents)} per hour, {money(tariff.dailyMaxCents)} daily max
    </p>
  </header>
  <BookingFlow {lot} {tariff} {spots} {spotsWindow} {prefillSpot} {vehicles} />
</div>
