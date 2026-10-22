<script lang="ts">
  import { router } from '@inertiajs/svelte'
  import type { Snippet } from 'svelte'
  import RefundMix from '@/components/charts/RefundMix.svelte'
  import SeriesChart from '@/components/charts/SeriesChart.svelte'
  import TopLots from '@/components/charts/TopLots.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import StatCard from '@/components/StatCard.svelte'
  import Select from '@/components/ui/select/Select.svelte'
  import Skeleton from '@/components/ui/skeleton/Skeleton.svelte'
  import { moneyCompact } from '@/lib/money'
  import type { AnalyticsSeriesPoint, KpiSet, LotOption, RefundMixRow, TopLotRow } from '@/types'

  /**
   * GET /admin/analytics?days=7|30|90&lot=: revenue, occupancy and sessions over the window.
   * kpis/series/topLots/refundMix are deferred and cached server-side, so each block shows a
   * skeleton of its own size until its data arrives.
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
    { value: '7', label: 'Last 7 days' },
    { value: '30', label: 'Last 30 days' },
    { value: '90', label: 'Last 90 days' },
  ]
</script>

<PageTitle title="Analytics" />

{#snippet panel(title: string, subtitle: string, body: Snippet)}
  <section class="bg-card shadow-card rounded-xl p-4 sm:p-5">
    <h2 class="text-lg font-bold">{title}</h2>
    <p class="text-muted-foreground text-sm">{subtitle}</p>
    <div class="mt-4">{@render body()}</div>
  </section>
{/snippet}

<div class="flex flex-col gap-6">
  <PageHeader title="Analytics">
    {#snippet actions()}
      <div class="flex flex-wrap items-center gap-2">
        <Select
          items={[{ value: '', label: 'All lots' }, ...lots.map((l) => ({ value: l.id, label: l.name }))]}
          value={lotFilter && !/^0+(-0+)*$/.test(lotFilter) ? lotFilter : ''}
          onValueChange={setLot}
          ariaLabel="Lot"
          class="sm:w-48"
        />
        <Select items={rangeItems} value={String(days)} onValueChange={(v: string) => setRange(Number(v))} ariaLabel="Date range" class="sm:w-40" />
      </div>
    {/snippet}
  </PageHeader>

  {#if kpis}
    <div class="grid grid-cols-2 gap-3 xl:grid-cols-4">
      <StatCard label="Revenue" value={moneyCompact(kpis.revenueCents)} hint="paid bookings" />
      <StatCard label="Average occupancy" value={`${kpis.occupancyAvgPct}%`} hint="across all bays" />
      <StatCard label="Sessions" value={kpis.sessions} hint={`${kpis.reservations} bookings`} />
      <StatCard label="No-shows" value={`${kpis.noShowPct}%`} hint={`${moneyCompact(kpis.refundsPaidCents)} refunded`} />
    </div>
  {:else}
    <div class="grid grid-cols-2 gap-3 xl:grid-cols-4">
      {#each Array(4) as _, i (i)}<Skeleton class="h-[7.25rem] rounded-xl" />{/each}
    </div>
  {/if}

  {#snippet revenue()}
    {#if series}<SeriesChart {series} label="Revenue" value={(d) => d.revenueCents} format={moneyCompact} color="var(--chart-1)" />{:else}<Skeleton class="h-56" />{/if}
  {/snippet}
  {#snippet occupancy()}
    {#if series}<SeriesChart {series} label="Occupancy" kind="line" max={100} value={(d) => d.occupancyPct} format={(n) => `${Math.round(n)}%`} color="var(--chart-3)" />{:else}<Skeleton class="h-56" />{/if}
  {/snippet}
  {#snippet sessions()}
    {#if series}<SeriesChart {series} label="Sessions" value={(d) => d.sessions} format={(n) => String(Math.round(n))} color="var(--chart-4)" height={176} />{:else}<Skeleton class="h-44" />{/if}
  {/snippet}
  {#snippet top()}
    {#if topLots}<TopLots lots={topLots} />{:else}<Skeleton class="h-44" />{/if}
  {/snippet}
  {#snippet refunds()}
    {#if refundMix}<RefundMix rows={refundMix} />{:else}<Skeleton class="h-32" />{/if}
  {/snippet}

  {@render panel('Revenue per day', 'Paid bookings, in dollars.', revenue)}
  {@render panel('Occupancy per day', 'Share of bays in use, 0 to 100%.', occupancy)}
  <div class="grid gap-6 lg:grid-cols-2">
    {@render panel('Sessions per day', 'Cars checked in at the gate.', sessions)}
    {@render panel('Top lots by revenue', 'Over the selected range.', top)}
  </div>
  {@render panel('Refund decisions', 'How requested refunds were resolved.', refunds)}
</div>
