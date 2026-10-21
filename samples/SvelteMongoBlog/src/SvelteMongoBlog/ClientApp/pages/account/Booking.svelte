<script lang="ts">
  import { useForm } from '@inertiajs/svelte'
  import { CalendarClock, CalendarRange, CarFront, CreditCard, FileClock, Loader2, MapPin, ShieldCheck } from '@lucide/svelte'
  import ConfirmDialog from '@/components/ConfirmDialog.svelte'
  import OccurrenceTable from '@/components/OccurrenceTable.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import PriceTag from '@/components/PriceTag.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Select from '@/components/ui/select/Select.svelte'
  import { buttonVariants } from '@/components/ui/button/variants'
  import { money } from '@/lib/money'
  import { cn } from '@/lib/utils'
  import { fmtDateTime, fmtRange } from '@/lib/time'
  import type { BookingView, PolicyRow } from '@/types'

  /**
   * GET /account/bookings/{id} — full booking record: window, price breakdown, payment,
   * refund, sessions and the Spot Cover policy with its claims.
   */
  let {
    booking,
    payment,
    refund,
    sessions,
    policy,
  }: {
    booking: BookingView
    payment: { id: string; method: string; amountCents: number; status: string; paidAt: string | null } | null
    refund: { id: string; rule: string; amountCents: number; status: string; createdAt: string } | null
    sessions: { id: string; spotId: string; plateNo: string; enteredAtUtc: string; exitedAtUtc: string | null; status: string }[]
    policy: (PolicyRow & { claims: { id: string; kind: string; claimedCents: number; payoutCents: number | null; status: string; createdAt: string; decisionNote: string | null }[] }) | null
  } = $props()

  const methodLabel: Record<string, string> = { card: 'Card', mobilemoney: 'Mobile money', payatgate: 'Pay at gate' }
  const ruleLabel: Record<string, string> = {
    '24h_full': 'Full refund (24h+ lead)',
    '4h_half': 'Half refund (4h+ lead)',
    late_none: 'Late cancellation',
    policy_payout: 'Spot Cover payout',
  }
  const kindLabel: Record<string, string> = { damage: 'Damage', theft: 'Theft', brokenwindow: 'Broken window', other: 'Other' }

  const cancellable = $derived(booking.status === 'confirmed' && booking.occurrences.some(o => o.status === 'scheduled'))
  const unpaid = $derived(payment !== null && payment.status === 'pending')

  const cancelForm = useForm({})
  const cancel = () => cancelForm.post(`/account/bookings/${booking.id}/cancel`)

  const payForm = useForm({ method: payment?.method === 'mobilemoney' ? 'mobile_money' : 'card' })
  const pay = () => payForm.post(`/account/bookings/${booking.id}/pay`)
</script>

<PageTitle title={`Booking · ${booking.lotName}`} />

<div class="flex flex-col gap-6">
  <PageHeader title={`Booking — ${booking.lotName}`} description={`Spot ${booking.spotLabel} · ${booking.windowType === 'recurring' ? `${booking.occurrenceCount} recurring windows` : 'single window'}`}>
    {#snippet actions()}
      <StatusBadge status={booking.status} />
      {#if cancellable}
        <ConfirmDialog
          title="Cancel this booking?"
          description="Refunds follow the matrix: 24h+ lead pays back in full, 4h+ pays half, later cancellations are charged."
          confirmLabel="Cancel booking"
          destructive
          onConfirm={cancel}
          triggerClass={cn(buttonVariants({ variant: 'outline' }), 'rounded-2xl')}
        >
          {#snippet trigger()}Cancel{/snippet}
        </ConfirmDialog>
      {/if}
    {/snippet}
  </PageHeader>

  <div class="grid gap-6 lg:grid-cols-[1fr_22rem]">
    <div class="flex flex-col gap-6">
      <!-- Window -->
      <section class="bg-card shadow-card rounded-3xl border p-5 sm:p-6">
        <h2 class="font-display flex items-center gap-2 text-base font-semibold">
          {#if booking.windowType === 'recurring'}<CalendarClock class="size-4" aria-hidden="true" />{:else}<CalendarRange class="size-4" aria-hidden="true" />{/if}
          {booking.windowType === 'recurring' ? 'Recurring weekly window' : 'Single window'}
        </h2>
        <dl class="mt-4 grid gap-x-8 gap-y-3 text-sm sm:grid-cols-2">
          <div>
            <dt class="text-muted-foreground text-xs">Span</dt>
            <dd class="font-mono mt-0.5 tabular-nums">{fmtRange(booking.startAtUtc, booking.endAtUtc)}</dd>
          </div>
          {#if booking.pattern}
            <div>
              <dt class="text-muted-foreground text-xs">Weekly pattern · UTC</dt>
              <dd class="font-mono mt-0.5 tabular-nums">
                {booking.pattern.timeFrom}–{booking.pattern.timeTo} · {booking.pattern.weeks} week{booking.pattern.weeks === 1 ? '' : 's'}
              </dd>
            </div>
          {/if}
          <div>
            <dt class="text-muted-foreground text-xs">Plate</dt>
            <dd class="font-mono mt-0.5 tracking-wide uppercase">{booking.plateNo ?? '—'}</dd>
          </div>
          <div>
            <dt class="text-muted-foreground text-xs">Booked</dt>
            <dd class="font-mono mt-0.5 tabular-nums">{fmtDateTime(booking.createdAt)}</dd>
          </div>
          {#if booking.note}
            <div class="sm:col-span-2">
              <dt class="text-muted-foreground text-xs">Note</dt>
              <dd class="mt-0.5">{booking.note}</dd>
            </div>
          {/if}
        </dl>
        {#if booking.occurrences.length > 1}
          <div class="mt-5 border-t pt-4">
            <h3 class="text-sm font-medium">Materialized windows</h3>
            <OccurrenceTable occurrences={booking.occurrences} class="mt-3" />
          </div>
        {/if}
      </section>

      <!-- Sessions on this booking -->
      {#if sessions.length}
        <section class="bg-card shadow-card overflow-hidden rounded-3xl border">
          <h2 class="font-display px-5 pt-5 pb-3 text-base font-semibold">Gate sessions</h2>
          <ul class="divide-y border-t">
            {#each sessions as session (session.id)}
              <li class="hover:bg-accent/40 flex items-center gap-3 px-5 py-3 text-sm transition-colors">
                <CarFront class="text-muted-foreground size-4 shrink-0" aria-hidden="true" />
                <span class="font-mono tabular-nums">{fmtDateTime(session.enteredAtUtc)}</span>
                <span class="text-muted-foreground">→</span>
                <span class="font-mono tabular-nums">{session.exitedAtUtc ? fmtDateTime(session.exitedAtUtc) : 'open'}</span>
                <span class="ml-auto"><StatusBadge status={session.status} size="xs" /></span>
              </li>
            {/each}
          </ul>
        </section>
      {/if}

      <!-- Spot Cover policy + claims -->
      {#if policy}
        <section class="bg-card shadow-card rounded-3xl border p-5 sm:p-6">
          <div class="flex flex-wrap items-center justify-between gap-3">
            <h2 class="font-display flex items-center gap-2 text-base font-semibold">
              <ShieldCheck class="text-spot-booked size-4" aria-hidden="true" /> Spot Cover
            </h2>
            <StatusBadge status={policy.status} />
          </div>
          <dl class="mt-4 grid gap-x-8 gap-y-3 text-sm sm:grid-cols-3">
            <div>
              <dt class="text-muted-foreground text-xs">Premium</dt>
              <dd><PriceTag cents={policy.premiumCents} size="lg" /></dd>
            </div>
            <div>
              <dt class="text-muted-foreground text-xs">Coverage up to</dt>
              <dd><PriceTag cents={policy.coverageCents} size="lg" /></dd>
            </div>
            <div>
              <dt class="text-muted-foreground text-xs">Active since</dt>
              <dd class="font-mono mt-1 tabular-nums">{fmtDateTime(policy.startedAt)}</dd>
            </div>
          </dl>
          {#if policy.claims.length}
            <div class="mt-5 border-t pt-4">
              <h3 class="text-sm font-medium">Claims</h3>
              <ul class="mt-3 grid gap-2">
                {#each policy.claims as claim (claim.id)}
                  <li class="bg-muted/50 flex flex-wrap items-center justify-between gap-2 rounded-xl px-3 py-2.5 text-sm">
                    <span class="font-medium">{kindLabel[claim.kind] ?? claim.kind}</span>
                    <span class="text-muted-foreground font-mono text-xs tabular-nums">{fmtDateTime(claim.createdAt)}</span>
                    <span class="font-mono tabular-nums">{money(claim.claimedCents)}{claim.payoutCents !== null ? ` → ${money(claim.payoutCents)}` : ''}</span>
                    <StatusBadge status={claim.status} size="xs" />
                  </li>
                {/each}
              </ul>
            </div>
          {/if}
        </section>
      {/if}
    </div>

    <!-- Payment rail -->
    <aside class="flex h-fit flex-col gap-4 lg:sticky lg:top-8">
      <section class="bg-card shadow-card rounded-3xl border p-5">
        <h2 class="font-display text-base font-semibold">Price</h2>
        <dl class="mt-4 space-y-2.5 text-sm">
          <div class="flex items-baseline justify-between gap-3">
            <dt class="text-muted-foreground">Parking</dt>
            <dd><PriceTag cents={booking.parkingCents} /></dd>
          </div>
          {#if booking.insuranceCents > 0}
            <div class="flex items-baseline justify-between gap-3">
              <dt class="text-muted-foreground">Spot Cover</dt>
              <dd><PriceTag cents={booking.insuranceCents} /></dd>
            </div>
          {/if}
          <div class="flex items-baseline justify-between gap-3 border-t pt-2.5">
            <dt class="font-medium">Total</dt>
            <dd><PriceTag cents={booking.totalCents} size="xl" /></dd>
          </div>
        </dl>

        {#if payment}
          <div class="bg-muted/50 mt-4 flex items-center justify-between gap-3 rounded-xl px-3 py-2.5 text-sm">
            <span class="text-muted-foreground inline-flex items-center gap-1.5">
              <CreditCard class="size-4" aria-hidden="true" /> {methodLabel[payment.method] ?? payment.method}
            </span>
            <span class="flex items-center gap-2">
              <PriceTag cents={payment.amountCents} />
              <StatusBadge status={payment.status} size="xs" />
            </span>
          </div>
          {#if unpaid}
            <form class="mt-4 grid gap-2" onsubmit={(e: SubmitEvent) => { e.preventDefault(); pay() }}>
              <Select
                items={[
                  { value: 'card', label: 'Card' },
                  { value: 'mobile_money', label: 'Mobile money' },
                ]}
                bind:value={payForm.method}
                ariaLabel="Payment method"
                class="w-full"
              />
              <Button type="submit" size="lg" class="w-full rounded-2xl" disabled={payForm.processing}>
                {#if payForm.processing}<Loader2 class="animate-spin" aria-hidden="true" />{/if}
                {payForm.processing ? 'Paying…' : `Pay ${money(booking.totalCents)}`}
              </Button>
              {#if payForm.errors.method}<p class="text-destructive text-sm" role="alert">{payForm.errors.method}</p>{/if}
            </form>
          {/if}
        {/if}

        {#if refund}
          <div class="bg-cta/10 border-cta/30 mt-4 rounded-xl border p-3 text-sm">
            <p class="flex items-center justify-between gap-2">
              <span class="font-medium">Refund · {ruleLabel[refund.rule] ?? refund.rule}</span>
              <StatusBadge status={refund.status} size="xs" />
            </p>
            <p class="text-muted-foreground mt-1"><PriceTag cents={refund.amountCents} class="text-foreground" /> · {fmtDateTime(refund.createdAt)}</p>
          </div>
        {/if}
      </section>

      <section class="bg-card shadow-card rounded-3xl border p-5">
        <h2 class="font-display flex items-center gap-2 text-base font-semibold">
          <MapPin class="text-muted-foreground size-4" aria-hidden="true" /> Lot
        </h2>
        <p class="mt-2 text-sm font-medium">{booking.lotName}</p>
        <p class="text-muted-foreground text-[13px]">Spot {booking.spotLabel}</p>
        <Button href="/account/bookings" variant="outline" size="sm" class="mt-4 w-full rounded-2xl">
          <FileClock class="size-4" aria-hidden="true" /> All bookings
        </Button>
      </section>
    </aside>
  </div>
</div>
