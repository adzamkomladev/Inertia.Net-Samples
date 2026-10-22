<script lang="ts">
  import { Link } from '@inertiajs/svelte'
  import { Car } from '@lucide/svelte'
  import Availability from '@/components/Availability.svelte'
  import DataTable from '@/components/DataTable.svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import PriceTag from '@/components/PriceTag.svelte'
  import StatCard from '@/components/StatCard.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import { money } from '@/lib/money'
  import { cn } from '@/lib/utils'
  import type { AdminOverview, LotCard } from '@/types'

  /** GET /admin: what needs a decision, today's numbers, and live availability per lot. */
  let { overview }: { overview: AdminOverview } = $props()

  const attention = $derived(overview.needsAttention)
  const queues = $derived([
    { count: attention.refundsPending, label: 'Refunds to decide', href: '/admin/refunds' },
    { count: attention.claimsPending, label: 'Spot Cover claims', href: '/admin/insurance' },
    { count: attention.overdueSessions, label: 'Overdue sessions', href: '/admin/sessions' },
    { count: attention.openIncidents, label: 'Open incidents', href: '/admin/incidents' },
  ])
</script>

<PageTitle title="Overview" />

{#snippet lotCell(lot: LotCard)}
  <Link href={`/admin/lots/${lot.id}`} class="font-semibold hover:underline underline-offset-4">{lot.name}</Link>
  <span class="text-muted-foreground block text-sm">{lot.area}</span>
{/snippet}
{#snippet rateCell(lot: LotCard)}<PriceTag cents={lot.ratePerHourCents} size="sm" /><span class="text-muted-foreground text-sm"> / h</span>{/snippet}
{#snippet availCell(lot: LotCard)}<Availability free={lot.free} total={lot.spots} size="sm" class="w-full min-w-44" />{/snippet}

<div class="flex flex-col gap-6">
  <PageHeader title="Overview" description="What needs a decision and how today is going." />

  <section aria-labelledby="queues-title">
    <h2 id="queues-title" class="sr-only">Needs attention</h2>
    <div class="grid grid-cols-2 gap-3 xl:grid-cols-4">
      {#each queues as queue (queue.label)}
        <Link
          href={queue.href}
          prefetch="hover"
          class={cn(
            'rounded-xl p-4 transition-shadow hover:shadow-lifted focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none sm:p-5',
            queue.count > 0 ? 'bg-cta/25' : 'bg-card shadow-card',
          )}
        >
          <span class="font-display block text-4xl leading-none font-bold tabular-nums">{queue.count}</span>
          <span class="mt-2 block text-sm font-medium">{queue.label}</span>
        </Link>
      {/each}
    </div>
  </section>

  <div class="grid grid-cols-2 gap-3 xl:grid-cols-4">
    <StatCard label="Bookings today" value={overview.today.reservations} />
    <StatCard label="Gate sessions today" value={overview.today.sessions} />
    <StatCard label="Revenue today" value={money(overview.today.revenueCents)} />
    <StatCard label="No-shows, last 24 h" value={attention.noShowsToday} hint="released after 30 min" />
  </div>

  <section class="bg-card shadow-card overflow-hidden rounded-xl" aria-labelledby="lots-title">
    <header class="flex items-center justify-between px-5 pt-5 pb-3">
      <h2 id="lots-title" class="text-xl font-bold">Lots</h2>
      <Link href="/admin/lots" prefetch="hover" class="text-primary inline-flex min-h-11 items-center font-semibold hover:underline underline-offset-4">Manage lots</Link>
    </header>
    {#if overview.lots.length}
      <DataTable
        rows={overview.lots}
        rowKey={(lot) => lot.id}
        columns={[
          { key: 'lot', header: 'Lot', primary: true, snippet: lotCell },
          { key: 'rate', header: 'Rate', snippet: rateCell },
          { key: 'avail', header: 'Availability', snippet: availCell },
        ]}
      />
    {:else}
      <EmptyState icon={Car} title="No lots yet" description="Create the first lot to open bookings.">
        <Button href="/admin/lots">Create a lot</Button>
      </EmptyState>
    {/if}
  </section>
</div>
