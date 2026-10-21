<script lang="ts">
  import { CalendarClock, CalendarX2 } from '@lucide/svelte'
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
</script>

<PageTitle title="Bookings" />

{#snippet totalCell(b: BookingView)}<PriceTag cents={b.totalCents} />{/snippet}
{#snippet statusCell(b: BookingView)}<StatusBadge status={b.status} size="xs" />{/snippet}
{#snippet coverCell(b: BookingView)}{#if b.policyStatus}<StatusBadge status={b.policyStatus} size="xs" />{:else}—{/if}{/snippet}

<div class="flex flex-col gap-6">
  <PageHeader title="Bookings" description="Every window you've reserved, newest first.">
    {#snippet actions()}<Button href="/lots" size="sm" class="rounded-2xl">New booking</Button>{/snippet}
  </PageHeader>

  {#if bookings.length}
    <div class="bg-card shadow-card overflow-hidden rounded-2xl border">
      <DataTable
        rows={bookings}
        rowKey={(b) => b.id}
        rowHref={(b) => `/account/bookings/${b.id}`}
        columns={[
          { key: 'lot', header: 'Lot · spot', primary: true, cell: (b: BookingView) => `${b.lotName} — ${b.spotLabel}` },
          { key: 'window', header: 'Window', cell: (b: BookingView) => (b.windowType === 'recurring'
              ? `${b.occurrenceCount}× weekly · ${fmtDate(b.startAtUtc)} → ${fmtDate(b.endAtUtc)}`
              : fmtWindow(b.startAtUtc, b.endAtUtc)) },
          { key: 'plate', header: 'Plate', desktopOnly: true, cell: (b: BookingView) => b.plateNo ?? '—' },
          { key: 'total', header: 'Total', align: 'right' as const, cell: (b: BookingView) => totalCell(b) },
          { key: 'status', header: 'Status', align: 'right' as const, cell: (b: BookingView) => statusCell(b) },
          { key: 'cover', header: 'Cover', align: 'right' as const, desktopOnly: true, cell: (b: BookingView) => coverCell(b) },
        ]}
      />
    </div>
  {:else}
    <div class="bg-card shadow-card rounded-3xl border">
      <EmptyState
        icon={CalendarX2}
        title="No bookings yet"
        description="Reserve a single window or a recurring weekly loop on any live deck."
        class="py-14"
      >
        <Button href="/lots" class="rounded-2xl">Find a deck</Button>
      </EmptyState>
    </div>
  {/if}

  <!-- Legend for window types -->
  <div class="text-muted-foreground flex flex-wrap gap-4 text-xs">
    <span class="inline-flex items-center gap-1.5"><CalendarClock class="size-3.5" aria-hidden="true" /> Recurring — same weekly loop, up to 8 weeks</span>
  </div>
</div>
