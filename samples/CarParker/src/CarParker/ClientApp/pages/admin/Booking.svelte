<script lang="ts">
  import { useForm } from '@inertiajs/svelte'
  import { ArrowLeft, Ban, Car } from '@lucide/svelte'
  import OccurrenceTable from '@/components/OccurrenceTable.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import PriceTag from '@/components/PriceTag.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Textarea from '@/components/ui/textarea/Textarea.svelte'
  import { money } from '@/lib/money'
  import { fmtDate } from '@/lib/time'
  import type { BookingView } from '@/types'

  /** GET /admin/bookings/{id} — the operator's view of one reservation. */
  let {
    booking,
  }: {
    booking: BookingView & {
      parkingCents: number
      insuranceCents: number
      user?: { id: string; name: string; email: string } | null
    }
  } = $props()

  const cancelForm = useForm({ reason: '' })
  const cancel = () => cancelForm.post(`/admin/bookings/${booking.id}/cancel`)
</script>

<PageTitle title={`Booking ${booking.lotName} · ${booking.spotLabel}`} />

<div class="flex flex-col gap-6">
  <a href="/admin/bookings" class="text-muted-foreground hover:text-foreground inline-flex w-fit items-center gap-1.5 text-sm">
    <ArrowLeft class="size-4" aria-hidden="true" /> All bookings
  </a>

  <PageHeader title={`${booking.lotName} — spot ${booking.spotLabel}`}>
    {#snippet actions()}
      <StatusBadge status={booking.status} />
    {/snippet}
  </PageHeader>

  <div class="grid gap-5 lg:grid-cols-3">
    <!-- summary card -->
    <section class="bg-card shadow-card rounded-xl p-5 lg:col-span-2">
      <dl class="grid grid-cols-2 gap-4 sm:grid-cols-3">
        <div>
          <dt class="text-muted-foreground text-xs font-medium">Customer</dt>
          <dd class="mt-1 text-sm font-medium">{booking.user?.name ?? '—'}</dd>
          <dd class="text-muted-foreground text-xs">{booking.user?.email}</dd>
        </div>
        <div>
          <dt class="text-muted-foreground text-xs font-medium">Plate</dt>
          <dd class="font-mono mt-1 text-sm font-medium">{booking.plateNo ?? '—'}</dd>
        </div>
        <div>
          <dt class="text-muted-foreground text-xs font-medium">Window</dt>
          <dd class="mt-1 text-sm font-medium">
            {booking.windowType === 'recurring'
              ? `${booking.occurrenceCount}× weekly`
              : `${fmtDate(booking.startAtUtc, true)} → ${fmtDate(booking.endAtUtc, true)}`}
          </dd>
        </div>
        <div>
          <dt class="text-muted-foreground text-xs font-medium">Parking</dt>
          <dd class="mt-1 text-sm font-medium">{money(booking.parkingCents)}</dd>
        </div>
        <div>
          <dt class="text-muted-foreground text-xs font-medium">Spot Cover</dt>
          <dd class="mt-1 text-sm font-medium">{money(booking.insuranceCents)}</dd>
        </div>
        <div>
          <dt class="text-muted-foreground text-xs font-medium">Total</dt>
          <dd class="mt-1"><PriceTag cents={booking.totalCents} /></dd>
        </div>
        {#if booking.cancelledAt}
          <div>
            <dt class="text-muted-foreground text-xs font-medium">Cancelled</dt>
            <dd class="text-destructive mt-1 text-sm font-medium">{fmtDate(booking.cancelledAt, true)}</dd>
            {#if booking.note}<dd class="text-muted-foreground text-xs">{booking.note}</dd>{/if}
          </div>
        {/if}
      </dl>
    </section>

    <!-- actions -->
    <section class="bg-card shadow-card flex flex-col gap-3 rounded-xl p-5">
      <h2 class="font-display flex items-center gap-2 text-base font-semibold">
        <Car class="size-4" aria-hidden="true" /> Operator actions
      </h2>
      <p class="text-muted-foreground text-sm">
        Cancelling frees the spot immediately and files the refund request per the policy rules.
      </p>
      {#if booking.status === 'confirmed'}
        <Textarea
          bind:value={cancelForm.reason}
          placeholder="Cancellation note (optional)"
          rows={3}
          maxlength={280}
        />
        <Button variant="destructive" onclick={cancel} disabled={cancelForm.processing}>
          {#if cancelForm.processing}<span class="animate-pulse">Working…</span>{:else}<Ban class="size-4" aria-hidden="true" /> Cancel booking{/if}
        </Button>
      {:else}
        <p class="text-muted-foreground text-sm">This booking is {booking.status}; no further actions.</p>
      {/if}
    </section>
  </div>

  <section class="bg-card shadow-card rounded-xl p-5">
    <h2 class="font-display text-base font-semibold">Occurrences</h2>
    <div class="mt-3">
      <OccurrenceTable occurrences={booking.occurrences} />
    </div>
  </section>
</div>
