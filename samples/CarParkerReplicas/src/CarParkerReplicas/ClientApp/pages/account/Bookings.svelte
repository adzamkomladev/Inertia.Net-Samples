<script lang="ts">
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
  import type { BookingView } from '@/types'

  /** GET /account/bookings — flat list, most recent first. */
  let { bookings }: { bookings: BookingView[] } = $props()
  let q = $state('')
  const filtered = $derived(bookings.filter(b => `${b.lotName} ${b.spotLabel} ${b.plateNo ?? ''} ${b.status}`.toLowerCase().includes(q.trim().toLowerCase())))
</script>

<PageTitle title="Bookings" />

{#snippet totalCell(b: BookingView)}<PriceTag cents={b.totalCents} />{/snippet}
{#snippet statusCell(b: BookingView)}<StatusBadge status={b.status} size="xs" />{/snippet}
{#snippet coverCell(b: BookingView)}{#if b.policyStatus}<StatusBadge status={b.policyStatus} size="xs" />{:else}—{/if}{/snippet}

<div class="flex flex-col gap-6">
  <PageHeader title="Bookings" description="Every window you've reserved, newest first.">
    {#snippet actions()}<Button href="/lots" size="sm" >New booking</Button>{/snippet}
  </PageHeader>

  <SearchField bind:value={q} label="Search your bookings" placeholder="Search lot, bay, plate or status…" />
  {#if filtered.length}
    <div class="bg-card shadow-card overflow-hidden rounded-xl">
      <DataTable
        rows={filtered}
        rowKey={(b) => b.id}
        rowHref={(b) => `/account/bookings/${b.id}`}
        columns={[
          { key: 'lot', header: 'Lot · spot', primary: true, cell: (b: BookingView) => `${b.lotName} — ${b.spotLabel}` },
          { key: 'window', header: 'Window', cell: (b: BookingView) => (b.windowType === 'recurring'
              ? `${b.occurrenceCount}× weekly · ${fmtDate(b.startAtUtc)} → ${fmtDate(b.endAtUtc)}`
              : fmtWindow(b.startAtUtc, b.endAtUtc)) },
          { key: 'plate', header: 'Plate', desktopOnly: true, cell: (b: BookingView) => b.plateNo ?? '—' },
          { key: 'total', header: 'Total', align: 'right' as const, snippet: totalCell },
          { key: 'status', header: 'Status', align: 'right' as const, snippet: statusCell },
          { key: 'cover', header: 'Cover', align: 'right' as const, desktopOnly: true, snippet: coverCell },
        ]}
      />
    </div>
  {:else}
    <div class="bg-card shadow-card rounded-xl">
      <EmptyState
        icon={CalendarX2}
        title={q ? 'No matching bookings' : 'No bookings yet'}
        description="Reserve a single window or a recurring weekly loop on any live deck."
        class="py-14"
      >
        <Button href="/lots" >Find a deck</Button>
      </EmptyState>
    </div>
  {/if}

  <!-- Legend for window types -->
  <div class="text-muted-foreground flex flex-wrap gap-4 text-xs">
    <span class="inline-flex items-center gap-1.5"><CalendarClock class="size-3.5" aria-hidden="true" /> Recurring — same weekly loop, up to 8 weeks</span>
  </div>
</div>
