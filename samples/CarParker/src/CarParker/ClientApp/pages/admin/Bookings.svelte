<script lang="ts">
  import { router } from '@inertiajs/svelte'
  import { CalendarRange } from '@lucide/svelte'
  import DataTable from '@/components/DataTable.svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import PriceTag from '@/components/PriceTag.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import Select from '@/components/ui/select/Select.svelte'
  import { fmtDate } from '@/lib/time'
  import type { BookingView } from '@/types'

  /** GET /admin/bookings — every reservation on the platform with a status filter. */
  let {
    bookings,
    statusFilter,
  }: {
    bookings: (BookingView & { userName?: string | null; userEmail?: string | null })[]
    statusFilter: string | null
  } = $props()

  const statusItems = [
    { value: '', label: 'Any status' },
    { value: 'confirmed', label: 'Confirmed' },
    { value: 'completed', label: 'Completed' },
    { value: 'cancelled', label: 'Cancelled' },
  ]

  const setStatus = (value: string) =>
    router.get('/admin/bookings', statusFilter || value ? { status: value || undefined } : {}, { preserveScroll: true })
</script>

<PageTitle title="All bookings" />

{#snippet totalCell(b: (typeof bookings)[number])}<PriceTag cents={b.totalCents} />{/snippet}
{#snippet statusCell(b: (typeof bookings)[number])}<StatusBadge status={b.status} size="xs" />{/snippet}
{#snippet customerCell(b: (typeof bookings)[number])}{b.userName ?? b.userEmail ?? '—'}{/snippet}
{#snippet windowCell(b: (typeof bookings)[number])}{b.windowType === 'recurring' ? (b.occurrenceCount ? `${b.occurrenceCount}× weekly` : "Weekly") : fmtDate(b.startAtUtc, true)}{/snippet}

<div class="flex flex-col gap-6">
  <PageHeader title="All bookings" description="Every reservation across decks, newest first." />

  {#if bookings.length}
    <div class="bg-card shadow-card overflow-hidden rounded-xl">
      <div class="border-b p-3">
        <Select items={statusItems} value={statusFilter ?? ''} onValueChange={setStatus} ariaLabel="Filter by status" placeholder="Any status" class="sm:w-44" />
      </div>
      <DataTable
        rows={bookings}
        rowKey={(b) => b.id}
        rowHref={(b) => `/admin/bookings/${b.id}`}
        columns={[
          { key: 'customer', header: 'Customer', primary: true, snippet: customerCell },
          { key: 'lot', header: 'Deck · spot', cell: (b) => `${b.lotName} — ${b.spotLabel}` },
          { key: 'window', header: 'Window', snippet: windowCell },
          { key: 'plate', header: 'Plate', desktopOnly: true, cell: (b) => b.plateNo ?? '—' },
          { key: 'total', header: 'Total', align: 'right' as const, snippet: totalCell },
          { key: 'status', header: 'Status', align: 'right' as const, snippet: statusCell },
        ]}
      />
    </div>
  {:else}
    <div class="bg-card shadow-card rounded-xl">
      <EmptyState icon={CalendarRange} title="No bookings" description={statusFilter ? `No ${statusFilter} bookings right now.` : 'Bookings land here the moment customers reserve.'} class="py-14" />
    </div>
  {/if}
</div>
