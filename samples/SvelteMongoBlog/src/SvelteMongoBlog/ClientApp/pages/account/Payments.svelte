<script lang="ts">
  import { CreditCard, ReceiptText } from '@lucide/svelte'
  import DataTable from '@/components/DataTable.svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import PriceTag from '@/components/PriceTag.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import { fmtDateTime } from '@/lib/time'
  import type { PaymentRow, RefundRow } from '@/types'

  /** GET /account/payments — payments + refunds with booking labels. */
  let {
    payments,
    refunds,
  }: {
    payments: PaymentRow[]
    refunds: RefundRow[]
  } = $props()

  const methodLabel: Record<string, string> = { card: 'Card', mobilemoney: 'Mobile money', payatgate: 'Pay at gate' }
  const ruleLabel: Record<string, string> = {
    '24h_full': 'Full refund',
    '4h_half': 'Half refund',
    late_none: 'Late cancellation',
    policy_payout: 'Spot Cover payout',
  }
</script>

<PageTitle title="Payments" />

{#snippet amountCell(p: PaymentRow)}<PriceTag cents={p.amountCents} />{/snippet}
{#snippet statusCell(p: PaymentRow)}<StatusBadge status={p.status} size="xs" />{/snippet}
{#snippet refundStatusCell(r: RefundRow)}<StatusBadge status={r.status} size="xs" />{/snippet}
{#snippet refundAmountCell(r: RefundRow)}<PriceTag cents={r.amountCents} />{/snippet}

<div class="flex flex-col gap-6">
  <PageHeader title="Payments" description="Charges and refunds across all your bookings." />

  {#if payments.length}
    <div class="bg-card shadow-card overflow-hidden rounded-2xl border">
      <DataTable
        rows={payments}
        rowKey={(p) => p.id}
        rowHref={(p) => `/account/bookings/${p.reservationId}`}
        columns={[
          { key: 'lot', header: 'Booking', primary: true, cell: (p: PaymentRow) => `${p.lotName ?? 'Booking'}${p.spotLabel ? ` · ${p.spotLabel}` : ''}` },
          { key: 'when', header: 'Date', cell: (p: PaymentRow) => fmtDateTime(p.paidAt ?? p.createdAt) },
          { key: 'method', header: 'Method', desktopOnly: true, cell: (p: PaymentRow) => methodLabel[p.method] ?? p.method },
          { key: 'amount', header: 'Amount', align: 'right' as const, cell: (p: PaymentRow) => amountCell(p) },
          { key: 'status', header: 'Status', align: 'right' as const, cell: (p: PaymentRow) => statusCell(p) },
        ]}
      />
    </div>

    {#if refunds.length}
      <section class="bg-card shadow-card overflow-hidden rounded-2xl border">
        <h2 class="font-display flex items-center gap-2 px-5 pt-5 pb-3 text-base font-semibold">
          <ReceiptText class="size-4" aria-hidden="true" /> Refunds
        </h2>
        <div class="border-t">
          <DataTable
            rows={refunds}
            rowKey={(r) => r.id}
            rowHref={(r) => `/account/bookings/${r.reservationId}`}
            columns={[
              { key: 'rule', header: 'Rule', primary: true, cell: (r: RefundRow) => ruleLabel[r.rule] ?? r.rule },
              { key: 'when', header: 'Requested', cell: (r: RefundRow) => fmtDateTime(r.createdAt) },
              { key: 'amount', header: 'Amount', align: 'right' as const, cell: (r: RefundRow) => refundAmountCell(r) },
              { key: 'status', header: 'Status', align: 'right' as const, cell: (r: RefundRow) => refundStatusCell(r) },
            ]}
          />
        </div>
      </section>
    {/if}
  {:else}
    <div class="bg-card shadow-card rounded-3xl border">
      <EmptyState icon={CreditCard} title="No payments yet" description="Charges appear here after your first booking — card, mobile money or pay at gate." class="py-14" />
    </div>
  {/if}
</div>
