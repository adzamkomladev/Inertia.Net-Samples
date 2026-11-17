<script lang="ts">
  import { Link } from '@inertiajs/svelte'
  import Pagination from '@/components/Pagination.svelte'
  import SearchField from '@/components/SearchField.svelte'
  import CalendarClock from '@lucide/svelte/icons/calendar-clock'
  import CalendarX2 from '@lucide/svelte/icons/calendar-x-2'
  import DataTable from '@/components/DataTable.svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import PriceTag from '@/components/PriceTag.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import { fmtDate, fmtWindow } from '@/lib/time'
  import { cn } from '@/lib/utils'
  import type { BookingView, BookingsListView } from '@/types'

  /**
   * GET /account/bookings?view=upcoming|past — 20 per page, paged on the server.
   * Upcoming: soonest next visit first. Past: latest end first. Search filters the page on screen.
   */
  let { bookings, page = 1, hasMore = false, view = 'upcoming' }: {
    bookings: BookingView[]
    page?: number
    hasMore?: boolean
    view?: BookingsListView
  } = $props()
  let q = $state('')
  const filtered = $derived(bookings.filter(b => `${b.lotName} ${b.spotLabel} ${b.plateNo ?? ''} ${b.status}`.toLowerCase().includes(q.trim().toLowerCase())))

  const views: { value: BookingsListView; label: string }[] = [
    { value: 'upcoming', label: 'Upcoming' },
    { value: 'past', label: 'Past' },
  ]

  /** Upcoming rows show the next window still to come; past rows show the whole booking. */
  const nextWindow = (b: BookingView) => {
    const now = Date.now()
    return b.occurrences.find(o => (o.status === 'scheduled' || o.status === 'active') && Date.parse(o.endAtUtc) >= now)
  }
  const whenText = (b: BookingView) => {
    if (view === 'upcoming') {
      const next = nextWindow(b)
      if (next) return `${fmtWindow(next.startAtUtc, next.endAtUtc, b.lotTimeZoneId)}${b.windowType === 'recurring' ? ' · weekly' : ''}`
    }
    return b.windowType === 'recurring'
      ? `${b.occurrenceCount}× weekly · ${fmtDate(b.startAtUtc ?? b.occurrences[0]?.startAtUtc, false, b.lotTimeZoneId)} to ${fmtDate(b.endAtUtc ?? b.occurrences.at(-1)?.endAtUtc, false, b.lotTimeZoneId)}`
      : fmtWindow(b.startAtUtc, b.endAtUtc, b.lotTimeZoneId)
  }
</script>

<PageTitle title="Bookings" />

{#snippet totalCell(b: BookingView)}<PriceTag cents={b.totalCents} />{/snippet}
{#snippet statusCell(b: BookingView)}<StatusBadge status={b.status} size="xs" />{/snippet}
{#snippet coverCell(b: BookingView)}{#if b.policyStatus}<StatusBadge status={b.policyStatus} size="xs" />{:else}—{/if}{/snippet}

<div class="flex flex-col gap-6">
  <PageHeader title="Bookings" description={view === 'upcoming' ? 'Bookings still to come, the next visit first.' : 'Finished and cancelled bookings, the latest first.'}>
    {#snippet actions()}<Button href="/lots" size="sm">New booking</Button>{/snippet}
  </PageHeader>

  <div class="flex flex-col gap-3 sm:flex-row sm:items-center">
    <nav aria-label="Booking lists" class="bg-muted flex w-full shrink-0 gap-1 rounded-xl p-1 sm:w-fit">
      {#each views as item (item.value)}
        <Link
          href={`/account/bookings?view=${item.value}`}
          prefetch="hover"
          cacheFor="30s"
          preserveScroll
          aria-current={view === item.value ? 'page' : undefined}
          class={cn(
            'focus-visible:ring-ring inline-flex min-h-11 flex-1 items-center justify-center rounded-lg px-5 text-sm font-semibold transition-colors focus-visible:ring-2 focus-visible:outline-none sm:flex-none md:min-h-10',
            view === item.value ? 'bg-card shadow-card text-foreground' : 'text-muted-foreground hover:text-foreground',
          )}
        >{item.label}</Link>
      {/each}
    </nav>
    <SearchField bind:value={q} label={page > 1 || hasMore ? 'Search bookings on this page' : 'Search your bookings'} placeholder="Search lot, bay, plate or status…" />
  </div>

  {#if filtered.length}
    <div class="bg-card shadow-card overflow-hidden rounded-xl">
      <DataTable
        compact
        rows={filtered}
        rowKey={(b) => b.id}
        rowHref={(b) => `/account/bookings/${b.id}`}
        columns={[
          { key: 'lot', header: 'Lot · bay', primary: true, cell: (b: BookingView) => `${b.lotName} · ${b.spotLabel}` },
          { key: 'window', header: view === 'upcoming' ? 'Next visit' : 'When', cell: whenText },
          { key: 'plate', header: 'Plate', desktopOnly: true, cell: (b: BookingView) => b.plateNo ?? '—' },
          { key: 'total', header: 'Total', align: 'right' as const, trailing: true, snippet: totalCell },
          { key: 'status', header: 'Status', align: 'right' as const, trailing: true, snippet: statusCell },
          { key: 'cover', header: 'Cover', align: 'right' as const, desktopOnly: true, snippet: coverCell },
        ]}
      />
      {#if page > 1 || hasMore}<Pagination href="/account/bookings" {page} {hasMore} params={{ view }} />{/if}
    </div>
  {:else}
    <div class="bg-card shadow-card rounded-xl">
      <EmptyState
        icon={CalendarX2}
        title={q ? 'No matching bookings' : view === 'upcoming' ? 'Nothing coming up' : 'No past bookings yet'}
        description={view === 'upcoming' ? 'Reserve a single window or the same time every week at any open lot.' : 'Bookings move here once they end or are cancelled.'}
        class="py-14"
      >
        {#if view === 'upcoming'}<Button href="/lots">Find a lot</Button>{/if}
      </EmptyState>
      {#if page > 1}<Pagination href="/account/bookings" {page} {hasMore} params={{ view }} />{/if}
    </div>
  {/if}

  <!-- Legend for window types -->
  <div class="text-muted-foreground flex flex-wrap gap-4 text-xs">
    <span class="inline-flex items-center gap-1.5"><CalendarClock class="size-3.5" aria-hidden="true" /> Weekly bookings repeat at the same time, up to 8 weeks</span>
  </div>
</div>
