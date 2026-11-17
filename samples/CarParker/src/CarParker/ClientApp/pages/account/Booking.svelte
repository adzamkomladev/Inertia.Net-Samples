<script lang="ts">
  import { untrack } from 'svelte'
  import { Link, useForm } from '@inertiajs/svelte'
  import ArrowLeft from '@lucide/svelte/icons/arrow-left'
  import CalendarClock from '@lucide/svelte/icons/calendar-clock'
  import CalendarRange from '@lucide/svelte/icons/calendar-range'
  import CarFront from '@lucide/svelte/icons/car-front'
  import CreditCard from '@lucide/svelte/icons/credit-card'
  import Download from '@lucide/svelte/icons/download'
  import Loader2 from '@lucide/svelte/icons/loader-circle'
  import MapPin from '@lucide/svelte/icons/map-pin'
  import ShieldCheck from '@lucide/svelte/icons/shield-check'
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
  import { fmtDateTime, fmtRange, fmtTime, fmtWindow, weekdayOf, zoneLabel, zonedParts } from '@/lib/time'
  import type { BookingView, CancelPreview, PolicyRow } from '@/types'

  /**
   * GET /account/bookings/{id} — full booking record: window, price breakdown, payment,
   * refund, sessions and the Spot Cover policy with its claims. Times read on the lot's clock.
   */
  let {
    booking,
    payment,
    refund,
    sessions,
    policy,
    cancelPreview = null,
  }: {
    booking: BookingView
    payment: { id: string; method: string; amountCents: number; status: string; paidAt: string | null } | null
    refund: { id: string; rule: string; amountCents: number; status: string; createdAt: string } | null
    sessions: { id: string; spotId: string; plateNo: string; enteredAtUtc: string; exitedAtUtc: string | null; status: string }[]
    policy: (PolicyRow & { claims: { id: string; kind: string; claimedCents: number; payoutCents: number | null; status: string; createdAt: string; decisionNote: string | null }[] }) | null
    /** What cancelling now would pay back, computed by the server's refund rules. */
    cancelPreview?: CancelPreview | null
  } = $props()

  const tz = $derived(booking.lotTimeZoneId ?? 'UTC')
  const weekDays = [[1, 'Mon'], [2, 'Tue'], [3, 'Wed'], [4, 'Thu'], [5, 'Fri'], [6, 'Sat'], [0, 'Sun']] as const
  const joinDays = (names: string[]) => (names.length > 1 ? `${names.slice(0, -1).join(', ')} & ${names.at(-1)}` : (names[0] ?? ''))

  /** "Wed 18 Nov, 17:12–19:12" for a single booking; "Tue & Thu, 17:00–19:00 · 4 weeks" for a weekly one. */
  const when = $derived.by(() => {
    if (booking.windowType !== 'recurring') return fmtWindow(booking.startAtUtc, booking.endAtUtc, tz)
    const first = booking.occurrences[0]
    if (!first) return `${booking.occurrenceCount} weekly visits`
    // Days and hours come from the dates themselves, on the lot's clock.
    const days = new Set(booking.occurrences.map(o => weekdayOf(zonedParts(new Date(o.startAtUtc), tz).date)))
    const weeks = booking.pattern?.weeks
    return `${joinDays(weekDays.filter(([n]) => days.has(n)).map(([, name]) => name))}, ${fmtTime(first.startAtUtc, tz)}–${fmtTime(first.endAtUtc, tz)}${weeks ? ` · ${weeks} week${weeks === 1 ? '' : 's'}` : ''}`
  })

  /** The cancel dialog states the money, not the rule. */
  const cancelText = $derived.by(() => {
    const preview = cancelPreview
    if (!preview) return 'The bay goes back to other drivers.'
    if (!preview.paid) return 'Nothing has been charged yet, so there is nothing to refund. The bay goes back to other drivers.'
    const reviewed = ' Our team reviews refunds, then pays them back to the same card or wallet.'
    if (preview.rule === '24h_full') return `You'll get ${money(preview.amountCents)} back, a full refund because it starts more than 24 hours from now.${reviewed}`
    if (preview.rule === '4h_half') return `You'll get ${money(preview.amountCents)} back, half refund because it starts in under 24 hours.${reviewed}`
    return 'No refund: it starts in under 4 hours.'
  })

  /** A refund receipt only exists for money that is coming back. */
  const refundDecided = $derived(refund !== null && (refund.status === 'approved' || refund.status === 'paid'))

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

  const payForm = useForm(untrack(() => ({ method: payment?.method === 'mobilemoney' ? 'mobile_money' : 'card' })))
  const pay = () => payForm.post(`/account/bookings/${booking.id}/pay`)
</script>

<PageTitle title={`Booking · ${booking.lotName}`} />

<div class="flex flex-col gap-6">
  <Link href="/account/bookings" prefetch="hover" class="text-muted-foreground hover:text-foreground -mb-3 -ml-1 inline-flex min-h-11 w-fit items-center gap-1.5 rounded-lg px-1 text-sm font-medium">
    <ArrowLeft class="size-4" aria-hidden="true" /> All bookings
  </Link>
  <PageHeader title={`Booking — ${booking.lotName}`} description={`Bay ${booking.spotLabel} · ${when}`}>
    {#snippet actions()}
      <StatusBadge status={booking.status} />
      {#if cancellable}
        <ConfirmDialog
          title="Cancel this booking?"
          description={cancelText}
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
      <section class="bg-card shadow-card rounded-xl p-5 sm:p-6">
        <h2 class="font-display flex items-center gap-2 text-base font-semibold">
          {#if booking.windowType === 'recurring'}<CalendarClock class="size-4" aria-hidden="true" />{:else}<CalendarRange class="size-4" aria-hidden="true" />{/if}
          {booking.windowType === 'recurring' ? 'Every week' : 'One visit'}
        </h2>
        <p class="font-display mt-3 text-2xl font-bold tabular-nums">{when}</p>
        <p class="text-muted-foreground mt-1 text-sm">Times are {zoneLabel(tz, booking.startAtUtc ?? booking.createdAt)}, the lot's local time.</p>
        <dl class="mt-4 grid gap-x-8 gap-y-3 text-sm sm:grid-cols-2">
          {#if booking.windowType === 'recurring' && booking.startAtUtc && booking.endAtUtc}
            <div>
              <dt class="text-muted-foreground text-xs">Dates</dt>
              <dd class="mt-0.5 tabular-nums">{fmtRange(booking.startAtUtc, booking.endAtUtc, tz)}</dd>
            </div>
          {/if}
          <div>
            <dt class="text-muted-foreground text-xs">Plate</dt>
            <dd class="plate mt-0.5 w-fit text-xs">{booking.plateNo ?? 'Not given'}</dd>
          </div>
          <div>
            <dt class="text-muted-foreground text-xs">Booked</dt>
            <dd class="mt-0.5 tabular-nums">{fmtDateTime(booking.createdAt, tz)}</dd>
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
            <h3 class="text-sm font-medium">Your dates</h3>
            <OccurrenceTable occurrences={booking.occurrences} timeZone={tz} class="mt-3" />
          </div>
        {/if}
      </section>

      <!-- Sessions on this booking -->
      {#if sessions.length}
        <section class="bg-card shadow-card overflow-hidden rounded-xl">
          <h2 class="font-display px-5 pt-5 pb-3 text-base font-semibold">Gate sessions</h2>
          <ul class="divide-y border-t">
            {#each sessions as session (session.id)}
              <li class="hover:bg-accent/40 flex items-center gap-3 px-5 py-3 text-sm transition-colors">
                <CarFront class="text-muted-foreground size-4 shrink-0" aria-hidden="true" />
                <span class="tabular-nums">{fmtDateTime(session.enteredAtUtc, tz)}</span>
                <span class="text-muted-foreground">to</span>
                <span class="tabular-nums">{session.exitedAtUtc ? fmtDateTime(session.exitedAtUtc, tz) : 'still parked'}</span>
                <span class="ml-auto"><StatusBadge status={session.status} size="xs" /></span>
              </li>
            {/each}
          </ul>
        </section>
      {/if}

      <!-- Spot Cover policy + claims -->
      {#if policy}
        <section class="bg-card shadow-card rounded-xl p-5 sm:p-6">
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
              <dd class="mt-1 tabular-nums">{fmtDateTime(policy.startedAt, tz)}</dd>
            </div>
          </dl>
          {#if policy.claims.length}
            <div class="mt-5 border-t pt-4">
              <h3 class="text-sm font-medium">Claims</h3>
              <ul class="mt-3 grid gap-2">
                {#each policy.claims as claim (claim.id)}
                  <li class="bg-muted/50 flex flex-wrap items-center justify-between gap-2 rounded-xl px-3 py-2.5 text-sm">
                    <span class="font-medium">{kindLabel[claim.kind] ?? claim.kind}</span>
                    <span class="text-muted-foreground text-xs tabular-nums">{fmtDateTime(claim.createdAt, tz)}</span>
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
      <section class="bg-card shadow-card rounded-xl p-5">
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
              <Button type="submit" size="lg" class="w-full" disabled={payForm.processing}>
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
            <p class="text-muted-foreground mt-1"><PriceTag cents={refund.amountCents} class="text-foreground" /> · requested {fmtDateTime(refund.createdAt, tz)}</p>
            {#if refund.status === 'requested'}
              <p class="text-muted-foreground mt-1">Refund requested, awaiting review.</p>
            {:else if refund.status === 'rejected'}
              <p class="text-muted-foreground mt-1">This refund was declined. Contact the lot if you think that is wrong.</p>
            {/if}
          </div>
        {/if}
      </section>

      <section class="bg-card shadow-card rounded-xl p-5">
        <h2 class="font-display text-base font-semibold">Receipts</h2>
        <div class="mt-3 grid gap-2">
          <!-- Plain links: the server streams a PDF attachment, which an Inertia visit must not intercept. -->
          <a href={`/account/bookings/${booking.id}/receipt.pdf`} download class={cn(buttonVariants({ variant: 'outline', size: 'sm' }), 'w-full')}>
            <Download class="size-4" aria-hidden="true" /> Download receipt
          </a>
          {#if refundDecided}
            <a href={`/account/bookings/${booking.id}/refund-receipt.pdf`} download class={cn(buttonVariants({ variant: 'outline', size: 'sm' }), 'w-full')}>
              <Download class="size-4" aria-hidden="true" /> Download refund receipt
            </a>
          {/if}
        </div>
      </section>

      <section class="bg-card shadow-card rounded-xl p-5">
        <h2 class="font-display flex items-center gap-2 text-base font-semibold">
          <MapPin class="text-muted-foreground size-4" aria-hidden="true" /> Lot
        </h2>
        <p class="mt-2 text-sm font-medium">{booking.lotName}</p>
        <p class="text-muted-foreground text-sm">Bay {booking.spotLabel}</p>
      </section>
    </aside>
  </div>
</div>
