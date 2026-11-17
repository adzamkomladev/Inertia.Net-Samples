<script lang="ts" module>
  import { refundCutoffs } from '@/lib/pricing'
  import { fmtDayTime, fmtTime, zonedParts } from '@/lib/time'

  export type Quote = {
    windows: number
    hours: number
    parkingCents: number
    uncappedCents: number
    capped: boolean
    coverCents: number
    totalCents: number
  }

  /**
   * The cancellation rule for this booking, with its real cutoffs on the lot's clock. `line` lists every tier
   * ("Free cancellation until Tue 17 Nov, 14:00 · half refund until Wed 10:00 · no refund after"); `short` is the
   * best tier still open, for the phone sticky bar.
   */
  export function refundRule(firstStart: Date | null, tz: string, payAtGate: boolean, now = Date.now()): { line: string; short: string } {
    if (payAtGate) return { line: 'Nothing is charged until you arrive, so cancelling costs nothing.', short: 'Pay at the gate · free to cancel' }
    if (!firstStart) {
      return { line: 'Free cancellation until 24 h before you arrive · half refund until 4 h before · no refund after', short: 'Free cancellation until 24 h before' }
    }
    const { fullUntil, halfUntil } = refundCutoffs(firstStart)
    const half = zonedParts(halfUntil, tz)
    if (now < fullUntil.getTime()) {
      const full = fmtDayTime(fullUntil.toISOString(), tz)
      const halfLabel = zonedParts(fullUntil, tz).date === half.date ? fmtTime(halfUntil.toISOString(), tz) : `${half.weekday} ${half.time}`
      return { line: `Free cancellation until ${full} · half refund until ${halfLabel} · no refund after`, short: `Free cancellation until ${full}` }
    }
    if (now < halfUntil.getTime()) {
      const until = fmtDayTime(halfUntil.toISOString(), tz)
      return { line: `Half refund until ${until} · no refund after (it starts in under 24 hours)`, short: `Half refund until ${until}` }
    }
    return { line: 'No refund if you cancel: it starts in under 4 hours.', short: 'No refund once reserved' }
  }
</script>

<script lang="ts">
  import CarFront from '@lucide/svelte/icons/car-front'
  import Clock3 from '@lucide/svelte/icons/clock-3'
  import CreditCard from '@lucide/svelte/icons/credit-card'
  import Loader2 from '@lucide/svelte/icons/loader-circle'
  import Undo2 from '@lucide/svelte/icons/undo-2'
  import Button from '@/components/ui/button/Button.svelte'
  import { money } from '@/lib/money'
  import type { LotCard, SpotView, Tariff } from '@/types'

  /**
   * Price summary: explains how the parking charge was reached (hours x hourly rate, or the
   * daily max when it is cheaper), what Spot Cover adds and the refund rule with this booking's
   * own cutoffs, then the Reserve button (desktop only: the phone sticky bar carries the action).
   */
  let {
    lot,
    tariff,
    spot,
    quote,
    cover,
    payAtGate,
    processing,
    disabledReason,
    when,
    refund,
    onsubmit,
  }: {
    lot: LotCard
    tariff: Tariff
    spot: SpotView | null
    quote: Quote
    cover: boolean
    payAtGate: boolean
    processing: boolean
    disabledReason: string | null
    /** "Wed 18 Nov, 14:00–18:00" on the lot's clock, empty until a window is valid. */
    when: string
    /** The refund rule line from refundRule(). */
    refund: string
    onsubmit: () => void
  } = $props()
</script>

<div class="bg-card shadow-card rounded-xl p-5">
  <h2 class="text-lg font-bold">Your reservation</h2>
  <p class="text-muted-foreground mt-0.5 text-sm">{lot.name}</p>
  <p class="mt-3 flex items-center gap-2 text-sm font-semibold">
    <CarFront class="size-4 shrink-0" aria-hidden="true" />
    {#if spot}Bay {spot.label} · level {spot.level}{:else}<span class="text-muted-foreground font-medium">No bay chosen yet</span>{/if}
  </p>
  {#if when}
    <p class="mt-1.5 flex items-center gap-2 text-sm font-semibold tabular-nums">
      <Clock3 class="size-4 shrink-0" aria-hidden="true" /> {when}
    </p>
  {/if}

  <dl class="mt-4 grid gap-2.5 border-t pt-4 text-sm">
    <div class="flex items-baseline justify-between gap-3">
      <dt>
        Parking
        <span class="text-muted-foreground block text-xs">
          {#if quote.windows}
            {quote.hours} h × {money(tariff.ratePerHourCents)}{quote.windows > 1 ? ` × ${quote.windows} visits` : ''}
          {:else}Choose a start and end time{/if}
        </span>
      </dt>
      <dd class="font-semibold tabular-nums">{money(quote.parkingCents)}</dd>
    </div>
    {#if quote.capped}
      <div class="bg-spot-free/20 text-spot-free-ink rounded-lg px-3 py-2 text-xs font-medium">
        Daily max applied: {money(tariff.dailyMaxCents)} per 24 h instead of {money(quote.uncappedCents)}.
      </div>
    {/if}
    {#if cover}
      <div class="flex items-baseline justify-between gap-3">
        <dt>
          Spot Cover
          <span class="text-muted-foreground block text-xs">{tariff.insurancePercent}% of parking, min {money(tariff.insuranceMinCents)}</span>
        </dt>
        <dd class="font-semibold tabular-nums">{money(quote.coverCents)}</dd>
      </div>
    {/if}
    <div class="flex items-baseline justify-between gap-3 border-t pt-3">
      <dt class="text-base font-bold">Total</dt>
      <dd class="font-display text-3xl font-bold tabular-nums">{money(quote.totalCents)}</dd>
    </div>
  </dl>

  <Button type="button" size="lg" class="mt-4 hidden w-full lg:inline-flex" disabled={processing} onclick={onsubmit}>
    {#if processing}<Loader2 class="animate-spin" aria-hidden="true" />{/if}
    {processing ? 'Reserving…' : `Reserve for ${money(quote.totalCents)}`}
  </Button>
  {#if disabledReason}<p class="text-destructive mt-2 hidden text-center text-sm lg:block" role="alert">{disabledReason}</p>{/if}
  <div class="mt-4 grid gap-2 text-xs leading-5">
    <p class="flex items-start gap-2">
      <Undo2 class="text-muted-foreground mt-0.5 size-3.5 shrink-0" aria-hidden="true" />
      <span><span class="font-semibold">If you cancel:</span> {refund}</span>
    </p>
    <p class="text-muted-foreground flex items-start gap-2">
      <CreditCard class="mt-0.5 size-3.5 shrink-0" aria-hidden="true" />
      {payAtGate ? 'Pay when you arrive at the gate.' : 'Charged when you reserve. Refunds are reviewed, then paid back to the same card or wallet.'}
    </p>
  </div>
</div>
