<script lang="ts">
  import { untrack } from 'svelte'
  import SearchField from '@/components/SearchField.svelte'
  import { useDebouncedSearch } from '@/lib/debounced-search.svelte'
  import { router } from '@inertiajs/svelte'
  import CalendarRange from '@lucide/svelte/icons/calendar-range'
  import DataTable from '@/components/DataTable.svelte'
  import Pagination from '@/components/Pagination.svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import PriceTag from '@/components/PriceTag.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import Select from '@/components/ui/select/Select.svelte'
  import { fmtWindow } from '@/lib/time'
  import type { BookingView } from '@/types'

  /** GET /admin/bookings — every reservation on the platform with a status filter. */
  let {
    query = '',
    bookings,
    statusFilter,
    page = 1,
    hasMore = false,
  }: {
    query?: string
    bookings: (BookingView & { userName?: string | null; userEmail?: string | null })[]
    statusFilter: string | null
    page: number
    hasMore: boolean
  } = $props()

  let q = $state(untrack(() => query))
  $effect.pre(() => { q = query })
  useDebouncedSearch(() => q, '/admin/bookings', () => ({ status: statusFilter }))

  const statusItems = [
    { value: '', label: 'Any status' },
    { value: 'confirmed', label: 'Confirmed' },
    { value: 'completed', label: 'Completed' },
    { value: 'cancelled', label: 'Cancelled' },
  ]

  const setStatus = (value: string) =>
    router.get('/admin/bookings', { q, status: value || undefined }, { preserveScroll: true })
</script>

<PageTitle title="All bookings" />

{#snippet totalCell(b: (typeof bookings)[number])}<PriceTag cents={b.totalCents} />{/snippet}
{#snippet statusCell(b: (typeof bookings)[number])}<StatusBadge status={b.status} size="xs" />{/snippet}
{#snippet customerCell(b: (typeof bookings)[number])}{b.userName ?? b.userEmail ?? '—'}{/snippet}
{#snippet windowCell(b: (typeof bookings)[number])}{b.windowType === 'recurring' ? (b.occurrenceCount ? `${b.occurrenceCount}× weekly` : "Weekly") : fmtWindow(b.startAtUtc, b.endAtUtc)}{/snippet}

<div class="flex flex-col gap-6">
  <PageHeader title="All bookings" description="Every reservation across decks, newest first." />

  <div class="bg-card flex flex-col gap-3 rounded-xl border p-4 sm:flex-row">
    <SearchField bind:value={q} label="Search bookings" placeholder="Search customer, email, lot or plate…" />
    <Select items={statusItems} value={statusFilter ?? ''} onValueChange={setStatus} ariaLabel="Filter by status" class="sm:w-44" />
  </div>
  <p class="text-muted-foreground text-sm" role="status">{bookings.length} bookings on this page</p>
  {#if bookings.length}
    <div class="bg-card shadow-card overflow-hidden rounded-xl">
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
      <EmptyState icon={CalendarRange} title={q ? 'No matching bookings' : 'No bookings'} description={q ? 'Try a different customer, email, lot or plate.' : statusFilter ? `No ${statusFilter} bookings right now.` : 'Bookings land here the moment customers reserve.'} class="py-14" />
    </div>
  {/if}
  {#if page > 1 || hasMore}<Pagination href="/admin/bookings" {page} {hasMore} params={{ q: query, status: statusFilter ?? '' }} />{/if}
</div>
