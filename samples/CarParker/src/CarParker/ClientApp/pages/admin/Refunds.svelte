<script lang="ts">
  import { useForm } from '@inertiajs/svelte'
  import { BadgeDollarSign, Check, ThumbsDown } from '@lucide/svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import PriceTag from '@/components/PriceTag.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import { money } from '@/lib/money'
  import { fmtDate } from '@/lib/time'
  import type { RefundRow } from '@/types'

  /** GET /admin/refunds — the approval queue and the decision log. */
  let {
    pending,
    decided,
  }: {
    pending: RefundRow[]
    decided: RefundRow[]
  } = $props()

  const approveForm = useForm({ amountCents: 0 })
  const rejectForm = useForm({ note: '' })
  const payForm = useForm({})
  let openId = $state<string | null>(null)

  const approve = (row: RefundRow) => {
    if (!approveForm.amountCents) approveForm.amountCents = row.amountCents
    approveForm.post(`/admin/refunds/${row.id}/approve`, { onFinish: () => { openId = null } })
  }
  const pay = (row: RefundRow) => payForm.post(`/admin/refunds/${row.id}/pay`, { onFinish: () => { openId = null } })
  const reject = (row: RefundRow) => rejectForm.post(`/admin/refunds/${row.id}/reject`, { onFinish: () => { openId = null } })
</script>

<PageTitle title="Refunds" />

<div class="flex flex-col gap-8">
  <PageHeader title="Refunds" description="Cancellation payouts to approve, pay out and audit." />

  <section class="flex flex-col gap-3">
    <h2 class="font-display flex items-center gap-2 text-base font-semibold">
      <BadgeDollarSign class="size-4" aria-hidden="true" /> Pending approval
      <span class="bg-cta/25 text-cta-foreground ml-1 rounded-full px-2 py-0.5 text-xs font-semibold">{pending.length}</span>
    </h2>
    {#if pending.length}
      <div class="flex flex-col gap-3">
        {#each pending as row (row.id)}
          {@const open = openId === row.id}
          <div class="bg-card shadow-card flex flex-col gap-3 rounded-xl p-4 lg:flex-row lg:items-center">
            <div class="min-w-0 flex-1">
              <div class="flex flex-wrap items-center gap-2">
                <span class="text-sm font-semibold">{row.lotName ?? 'Unknown lot'} · spot {row.spotLabel ?? '—'}</span>
                <StatusBadge status={row.status} size="xs" />
                <span class="text-muted-foreground text-xs">{row.rule.replace('_', ' ')}</span>
              </div>
              <p class="text-muted-foreground mt-1 text-xs">
                {row.requestedByName ?? 'Customer'} · requested {fmtDate(row.createdAt, true)}
                {#if row.reason}· “{row.reason}”{/if}
              </p>
            </div>
            <div class="flex items-center gap-2">
              <PriceTag cents={row.amountCents} />
              <Button size="sm" variant="outline" onclick={() => (openId = open ? null : row.id)}>Review</Button>
            </div>
          </div>
          {#if open}
            <div class="bg-muted/40 grid gap-4 rounded-2xl border p-4 lg:grid-cols-2">
              <form
                class="flex flex-col gap-2"
                onsubmit={(e: SubmitEvent) => { e.preventDefault(); approve(row) }}
              >
                <label class="text-sm font-medium" for={`amount-${row.id}`}>Approve & pay (max {money(row.maxCents ?? row.amountCents)})</label>
                <div class="flex items-center gap-2">
                  <Input id={`amount-${row.id}`} type="number" min="0" max={row.maxCents ?? row.amountCents} bind:value={approveForm.amountCents} class="w-32" />
                  <Button type="submit" size="sm" disabled={approveForm.processing}><Check class="size-3.5" aria-hidden="true" /> Approve</Button>
                </div>
                <p class="text-muted-foreground text-xs">Approving locks the amount; “pay” marks the money out the door.</p>
              </form>
              <form
                class="flex flex-col gap-2"
                onsubmit={(e: SubmitEvent) => { e.preventDefault(); reject(row) }}
              >
                <label class="text-sm font-medium" for={`note-${row.id}`}>…or reject with a note</label>
                <div class="flex items-center gap-2">
                  <Input id={`note-${row.id}`} bind:value={rejectForm.note} placeholder="Why this refund is rejected" maxlength={200} class="flex-1" />
                  <Button type="submit" size="sm" variant="destructive" disabled={rejectForm.processing}>
                    <ThumbsDown class="size-3.5" aria-hidden="true" /> Reject
                  </Button>
                </div>
              </form>
            </div>
          {/if}
        {/each}
      </div>
    {:else}
      <EmptyState icon={BadgeDollarSign} title="Queue is clear" description="Cancellations land here as refund requests to review." />
    {/if}
  </section>

  <section class="flex flex-col gap-3">
    <h2 class="font-display text-base font-semibold">Decision log</h2>
    {#if decided.length}
      <div class="bg-card shadow-card overflow-hidden rounded-xl">
        <ul class="divide-y">
          {#each decided as row (row.id)}
            <li class="flex flex-wrap items-center gap-3 p-4 text-sm">
              <StatusBadge status={row.status} size="xs" />
              <span class="font-medium">{row.lotName ?? '—'} · {row.spotLabel ?? '—'}</span>
              <PriceTag cents={row.amountCents} />
              <span class="text-muted-foreground">{fmtDate(row.paidAt ?? row.createdAt, true)}</span>
              {#if row.decisionNote}<span class="text-muted-foreground truncate">— {row.decisionNote}</span>{/if}
              {#if row.status === 'approved'}
                <Button size="sm" class="ml-auto" onclick={() => pay(row)} disabled={payForm.processing}>Mark paid</Button>
              {/if}
            </li>
          {/each}
        </ul>
      </div>
    {:else}
      <EmptyState icon={BadgeDollarSign} title="No decisions yet" description="Approved, paid and rejected refunds will be logged here." />
    {/if}
  </section>
</div>
