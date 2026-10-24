<script lang="ts">
  import CreditCard from '@lucide/svelte/icons/credit-card'
  import Landmark from '@lucide/svelte/icons/landmark'
  import Smartphone from '@lucide/svelte/icons/smartphone'
  import PriceTag from '@/components/PriceTag.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import { fmtDateTime } from '@/lib/time'
  import type { PaymentRow, RefundRow } from '@/types'

  /** Payments with method icons + linked refunds, one responsive list for /account/payments. */
  let {
    payments,
    refunds,
  }: {
    payments: PaymentRow[]
    refunds: RefundRow[]
  } = $props()

  const methodIcon = { card: CreditCard, mobilemoney: Smartphone, payatgate: Landmark }
  const methodLabel: Record<string, string> = { card: 'Card', mobilemoney: 'Mobile money', payatgate: 'Pay at gate' }
  const ruleLabel: Record<string, string> = {
    '24h_full': 'Full refund (24h+)',
    '4h_half': 'Half refund (4h+)',
    late_none: 'Late cancellation',
    policy_payout: 'Spot Cover payout',
  }
</script>

{#if !payments.length}
  <div class="text-muted-foreground py-12 text-center text-sm">No payments yet.</div>
{:else}
  <div class="overflow-hidden rounded-2xl border">
    <ul class="divide-y">
      {#each payments as payment (payment.id)}
        {@const Icon = (methodIcon as Record<string, any>)[payment.method] ?? CreditCard}
        {@const refund = refunds.find(r => r.reservationId === payment.reservationId)}
        <li class="bg-card hover:bg-accent/40 flex flex-col gap-2 px-4 py-3.5 transition-colors sm:flex-row sm:items-center sm:gap-4 sm:px-5">
          <span class="bg-muted text-muted-foreground flex size-9 shrink-0 items-center justify-center rounded-xl" aria-hidden="true">
            <Icon class="size-4" strokeWidth={1.75} />
          </span>
          <div class="min-w-0 flex-1">
            <p class="truncate text-sm font-medium">
              {payment.lotName ?? 'Booking'}
              {#if payment.spotLabel}<span class="text-muted-foreground"> · spot {payment.spotLabel}</span>{/if}
            </p>
            <p class="text-muted-foreground mt-0.5 text-[12px]">
              {methodLabel[payment.method] ?? payment.method} · {fmtDateTime(payment.paidAt ?? payment.createdAt)}
            </p>
          </div>
          <div class="flex items-center justify-between gap-3 sm:justify-end">
            <PriceTag cents={payment.amountCents} size="md" />
            <StatusBadge status={payment.status} />
          </div>
          {#if refund}
            <p class="text-muted-foreground text-[12px] sm:w-44 sm:text-right">
              Refund: <PriceTag cents={refund.amountCents} class="text-foreground" /> {ruleLabel[refund.rule] ?? refund.rule}
            </p>
          {/if}
        </li>
      {/each}
    </ul>
  </div>
{/if}
