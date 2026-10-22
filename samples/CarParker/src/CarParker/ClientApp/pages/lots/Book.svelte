<script lang="ts">
  import { ArrowLeft } from '@lucide/svelte'
  import BookingFlow from '@/components/BookingFlow.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import { money } from '@/lib/money'
  import type { LotCard, SpotView, Tariff } from '@/types'

  /** GET /lots/{id}/book: when, bay, cover, pay, beside a sticky price summary. */
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
</script>

<PageTitle title={`Book · ${lot.name}`} />

<div class="mx-auto w-full max-w-6xl px-4 pt-6 pb-28 sm:px-6 lg:pb-12">
  <a href={`/lots/${lot.id}`} class="text-muted-foreground hover:text-foreground -ml-1 inline-flex min-h-11 items-center gap-1.5 rounded-lg px-1 text-sm font-medium">
    <ArrowLeft class="size-4" aria-hidden="true" /> {lot.name}
  </a>
  <header class="mt-1 mb-6">
    <h1 class="text-3xl font-bold tracking-tight">Reserve a bay</h1>
    <p class="text-muted-foreground mt-1">
      {lot.name}, {lot.address} · {money(tariff.ratePerHourCents)} per hour, {money(tariff.dailyMaxCents)} daily max
    </p>
  </header>
  <BookingFlow {lot} {tariff} {spots} {prefillSpot} />
</div>
