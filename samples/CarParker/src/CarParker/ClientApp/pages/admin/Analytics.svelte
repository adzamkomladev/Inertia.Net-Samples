<script lang="ts">
  import { BarChart3 } from '@lucide/svelte'
  import OccupancyChart from '@/components/charts/OccupancyChart.svelte'
  import RefundMix from '@/components/charts/RefundMix.svelte'
  import TopLots from '@/components/charts/TopLots.svelte'
  import TrendChart from '@/components/charts/TrendChart.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import StatCard from '@/components/StatCard.svelte'
  import Select from '@/components/ui/select/Select.svelte'
  import Skeleton from '@/components/ui/skeleton/Skeleton.svelte'
  import { router } from '@inertiajs/svelte'
  import { moneyCompact } from '@/lib/money'
  import type { AnalyticsSeriesPoint, KpiSet, LotOption, RefundMixRow, TopLotRow } from '@/types'

  /**
   * GET /admin/analytics?days=7|30|90&lot= — revenue/occupancy over the window.
   * kpis/series/topLots/refundMix are deferred + cached server-side (60 s).
   */
  let {
    days,
    lotFilter,
    lots,
    kpis,
    series,
    topLots,
    refundMix,
  }: {
    days: number
    lotFilter: string // Guid.Empty = all lots
    lots: LotOption[]
    kpis: KpiSet | null
    series: AnalyticsSeriesPoint[] | null
    topLots: TopLotRow[] | null
    refundMix: RefundMixRow[] | null
  } = $props()

  const setRange = (value: number) =>
    router.get('/admin/analytics', { days: value, lot: lotFilter === '' ? undefined : lotFilter }, { preserveScroll: true })
  const setLot = (value: string) =>
    router.get('/admin/analytics', { days, lot: value === '' ? undefined : value }, { preserveScroll: true })

  const rangeItems = [
    { value: '7', label: '7 days' },
    { value: '30', label: '30 days' },
    { value: '90', label: '90 days' },
  ]
</script>

<PageTitle title="Analytics" />

<div class="flex flex-col gap-6">
  <PageHeader title="Analytics" description="Revenue, occupancy and refund mix across decks.">
    {#snippet actions()}
      <div class="flex items-center gap-2">
        <Select
          items={[{ value: '', label: 'All lots' }, ...lots.map((l) => ({ value: l.id, label: l.name }))]}
          value={lotFilter ?? ''}
          onValueChange={setLot}
          ariaLabel="Filter by lot"
          placeholder="All lots"
          class="sm:w-44"
        />
        <Select items={rangeItems} value={String(days)} onValueChange={(v: string) => setRange(Number(v))} ariaLabel="Range" placeholder="30 days" class="sm:w-32" />
      </div>
    {/snippet}
  </PageHeader>

  {#if kpis}
    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <StatCard label="Revenue" value={moneyCompact(kpis.revenueCents)} icon={BarChart3} hint="paid bookings in range" />
      <StatCard label="Sessions" value={kpis.sessions} hint={`${kpis.reservations} bookings`} />
      <StatCard label="Occupancy" value={`${kpis.occupancyAvgPct}%`} hint="average across spots" />
      <StatCard label="No-shows" value={`${kpis.noShowPct}%`} hint={`${moneyCompact(kpis.refundsPaidCents)} refunded`} />
    </div>
  {:else}
    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      {#each Array(4) as _, i (i)}
        <Skeleton class="h-28 rounded-3xl" />
      {/each}
    </div>
  {/if}

  {#if series}
    <div class="bg-card shadow-card rounded-3xl border p-5">
      <h2 class="font-display text-base font-semibold">Revenue & occupancy</h2>
      <div class="mt-4">
        <TrendChart {series} />
      </div>
    </div>
  {:else}
    <Skeleton class="h-80 rounded-3xl" />
  {/if}

  <div class="grid gap-5 lg:grid-cols-2">
    <div class="bg-card shadow-card rounded-3xl border p-5">
      <h2 class="font-display text-base font-semibold">Occupancy</h2>
      <div class="mt-4">{#if series}<OccupancyChart {series} />{:else}<Skeleton class="h-56 rounded-2xl" />{/if}</div>
    </div>
    <div class="bg-card shadow-card rounded-3xl border p-5">
      <h2 class="font-display text-base font-semibold">Top lots</h2>
      <div class="mt-4">{#if topLots}<TopLots lots={topLots} />{:else}<Skeleton class="h-56 rounded-2xl" />{/if}</div>
    </div>
  </div>

  <div class="bg-card shadow-card rounded-3xl border p-5">
    <h2 class="font-display text-base font-semibold">Refund mix</h2>
    <div class="mt-4">{#if refundMix}<RefundMix rows={refundMix} />{:else}<Skeleton class="h-40 rounded-2xl" />{/if}</div>
  </div>

  <p class="text-muted-foreground text-xs">
    Deferred props are cached server-side for 60 seconds (Inertia.Net 1.4 cached props) — the first visitor pays the
    query cost, the rest of the minute is free.
  </p>
</div>
