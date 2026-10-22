<script lang="ts" module>
  export type Quote = {
    windows: number
    hours: number
    parkingCents: number
    uncappedCents: number
    capped: boolean
    coverCents: number
    totalCents: number
  }
</script>

<script lang="ts">
  import { CarFront, CreditCard, Loader2 } from '@lucide/svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import { money } from '@/lib/money'
  import type { LotCard, SpotView, Tariff } from '@/types'

  /**
   * Price summary: explains how the parking charge was reached (hours x hourly rate, or the
   * daily max when it is cheaper) and what Spot Cover adds, then the Reserve button.
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
    onsubmit: () => void
  } = $props()
</script>

<div class="bg-card shadow-card rounded-xl p-5">
  <h2 class="text-lg font-bold">Your reservation</h2>
  <p class="text-muted-foreground mt-0.5 text-sm">{lot.name}</p>
  <p class="mt-3 flex items-center gap-2 text-sm font-semibold">
    <CarFront class="size-4" aria-hidden="true" />
    {#if spot}Bay {spot.label} · level {spot.level}{:else}<span class="text-muted-foreground font-medium">No bay chosen yet</span>{/if}
  </p>

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
      <div class="bg-spot-free/20 text-spot-free-foreground rounded-lg px-3 py-2 text-xs font-medium">
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

  <Button type="button" size="lg" class="mt-4 w-full" disabled={processing || !!disabledReason} onclick={onsubmit}>
    {#if processing}<Loader2 class="animate-spin" aria-hidden="true" />{/if}
    {processing ? 'Reserving…' : `Reserve for ${money(quote.totalCents)}`}
  </Button>
  {#if disabledReason}<p class="text-muted-foreground mt-2 text-center text-sm">{disabledReason}</p>{/if}
  <p class="text-muted-foreground mt-3 flex items-start gap-2 text-xs leading-5">
    <CreditCard class="mt-0.5 size-3.5 shrink-0" aria-hidden="true" />
    {payAtGate ? 'Pay when you arrive at the gate.' : 'Charged when you reserve.'} Cancel 24 h or more ahead for a full refund.
  </p>
</div>
