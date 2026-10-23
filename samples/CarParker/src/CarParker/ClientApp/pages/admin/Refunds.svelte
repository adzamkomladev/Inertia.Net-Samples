<script lang="ts">
  import { useForm } from '@inertiajs/svelte'
  import BadgeDollarSign from '@lucide/svelte/icons/badge-dollar-sign'
  import Check from '@lucide/svelte/icons/check'
  import ThumbsDown from '@lucide/svelte/icons/thumbs-down'
  import EmptyState from '@/components/EmptyState.svelte'
  import Pagination from '@/components/Pagination.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import PriceTag from '@/components/PriceTag.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import Textarea from '@/components/ui/textarea/Textarea.svelte'
  import MoneyInput from '@/components/MoneyInput.svelte'
  import SidePanel from '@/components/SidePanel.svelte'
  import Banknote from '@lucide/svelte/icons/banknote'
  import Loader2 from '@lucide/svelte/icons/loader-circle'
  import { money } from '@/lib/money'
  import { fmtDate } from '@/lib/time'
  import type { RefundRow } from '@/types'

  /** GET /admin/refunds — the approval queue and the decision log. */
  let {
    pending,
    decided,
    pendingPage = 1,
    pendingHasMore = false,
    decidedPage = 1,
    decidedHasMore = false,
  }: {
    pending: RefundRow[]
    decided: RefundRow[]
    pendingPage: number
    pendingHasMore: boolean
    decidedPage: number
    decidedHasMore: boolean
  } = $props()

  const approveForm = useForm({ amountCents: 0 })
  const rejectForm = useForm({ note: '' })
  const payForm = useForm({})
  let reviewing = $state<RefundRow | null>(null)
  let panelOpen = $state(false)
  let payingId = $state<string | null>(null)

  const review = (row: RefundRow) => {
    reviewing = row
    approveForm.amountCents = row.amountCents
    rejectForm.reset()
    approveForm.clearErrors()
    rejectForm.clearErrors()
    panelOpen = true
  }
  const close = () => (panelOpen = false)
  const approve = (row: RefundRow) => approveForm.post(`/admin/refunds/${row.id}/approve`, { preserveScroll: true, onSuccess: close })
  const reject = (row: RefundRow) => rejectForm.post(`/admin/refunds/${row.id}/reject`, { preserveScroll: true, onSuccess: close })
  const pay = (row: RefundRow) => {
    payingId = row.id
    payForm.post(`/admin/refunds/${row.id}/pay`, { preserveScroll: true, onFinish: () => (payingId = null) })
  }
  const ruleLabel: Record<string, string> = { '24h_full': 'Full refund · 24h+ notice', '4h_half': 'Half refund · 4h+ notice', late_none: 'Late cancellation', policy_payout: 'Spot Cover payout' }
  const presets = (row: RefundRow) => {
    const max = row.maxCents ?? row.amountCents
    return [{ label: 'Suggested', cents: row.amountCents }, { label: 'Half', cents: Math.round(max / 2) }, { label: 'Full', cents: max }]
  }
</script>

<PageTitle title="Refunds" />

<div class="flex flex-col gap-8">
  <PageHeader title="Refunds" description="Cancellation payouts to approve, pay out and audit." />

  <section class="flex flex-col gap-3">
    <h2 class="font-display flex items-center gap-2 text-base font-semibold">
      <BadgeDollarSign class="size-4" aria-hidden="true" /> Pending approval
      <span class="bg-cta/25 text-cta-foreground ml-1 rounded-full px-2 py-0.5 text-xs font-semibold">{pending.length} on page</span>
    </h2>
    {#if pending.length}
      <div class="flex flex-col gap-3">
        {#each pending as row (row.id)}
          <div class="bg-card shadow-card animate-rise flex flex-col gap-3 rounded-xl p-4 lg:flex-row lg:items-center">
            <div class="min-w-0 flex-1">
              <div class="flex flex-wrap items-center gap-2">
                <span class="text-sm font-semibold">{row.lotName ?? 'Unknown lot'} · bay {row.spotLabel ?? '—'}</span>
                <StatusBadge status={row.status} size="xs" />
                <span class="text-muted-foreground text-xs">{ruleLabel[row.rule] ?? row.rule}</span>
              </div>
              <p class="text-muted-foreground mt-1 text-xs">
                {row.requestedByName ?? 'Customer'} · requested {fmtDate(row.createdAt, true)}
                {#if row.reason}· “{row.reason}”{/if}
              </p>
            </div>
            <div class="flex items-center gap-3">
              <PriceTag cents={row.amountCents} />
              <Button size="sm" variant="outline" onclick={() => review(row)}>Review</Button>
            </div>
          </div>
        {/each}
      </div>
    {:else}
      <EmptyState icon={BadgeDollarSign} title="Queue is clear" description="Cancellations land here as refund requests to review." />
    {/if}
    {#if pendingPage > 1 || pendingHasMore}<Pagination href="/admin/refunds" page={pendingPage} hasMore={pendingHasMore} pageParam="pendingPage" params={{ decidedPage }} />{/if}
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
                <Button size="sm" class="ml-auto" onclick={() => pay(row)} disabled={payForm.processing}>
                  {#if payingId === row.id}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<Banknote class="size-4" aria-hidden="true" />{/if} Mark paid
                </Button>
              {/if}
            </li>
          {/each}
        </ul>
      </div>
    {:else}
      <EmptyState icon={BadgeDollarSign} title="No decisions yet" description="Approved, paid and rejected refunds will be logged here." />
    {/if}
    {#if decidedPage > 1 || decidedHasMore}<Pagination href="/admin/refunds" page={decidedPage} hasMore={decidedHasMore} pageParam="decidedPage" params={{ pendingPage }} />{/if}
  </section>
</div>

<SidePanel bind:open={panelOpen} title="Review refund" description="Approve an amount up to what was paid, or reject it with a reason the customer will see.">
  {#if reviewing}
    {@const max = reviewing.maxCents ?? reviewing.amountCents}
    <div class="grid gap-6">
      <dl class="bg-muted/60 grid grid-cols-2 gap-x-4 gap-y-3 rounded-xl p-4 text-sm">
        <div><dt class="text-muted-foreground text-xs">Lot and bay</dt><dd class="font-medium">{reviewing.lotName ?? '—'} · {reviewing.spotLabel ?? '—'}</dd></div>
        <div><dt class="text-muted-foreground text-xs">Customer</dt><dd class="font-medium">{reviewing.requestedByName ?? 'Customer'}</dd></div>
        <div><dt class="text-muted-foreground text-xs">Rule</dt><dd class="font-medium">{ruleLabel[reviewing.rule] ?? reviewing.rule}</dd></div>
        <div><dt class="text-muted-foreground text-xs">Requested</dt><dd class="font-medium">{fmtDate(reviewing.createdAt, true)}</dd></div>
        {#if reviewing.reason}<div class="col-span-2"><dt class="text-muted-foreground text-xs">Reason</dt><dd>“{reviewing.reason}”</dd></div>{/if}
      </dl>

      <form id="approve-refund" novalidate class="grid gap-3" onsubmit={(e: SubmitEvent) => { e.preventDefault(); approve(reviewing!) }}>
        <div class="flex items-baseline justify-between">
          <Label for="refund-amount">Amount to refund</Label>
          <span class="text-muted-foreground text-xs">Paid {money(max)}</span>
        </div>
        <MoneyInput id="refund-amount" bind:cents={approveForm.amountCents} {max} invalid={!!approveForm.errors.amountCents} />
        <div class="flex flex-wrap gap-2" role="group" aria-label="Quick amounts">
          {#each presets(reviewing) as preset (preset.label)}
            <button
              type="button"
              aria-pressed={approveForm.amountCents === preset.cents}
              onclick={() => (approveForm.amountCents = preset.cents)}
              class={['rounded-full px-3 py-1 text-xs font-semibold ring-1 ring-inset transition-[background-color,transform] active:scale-95', approveForm.amountCents === preset.cents ? 'bg-primary text-primary-foreground ring-primary' : 'ring-input hover:bg-muted']}
            >{preset.label} · {money(preset.cents)}</button>
          {/each}
        </div>
        {#if approveForm.errors.amountCents}<p class="text-destructive animate-rise text-sm" role="alert">{approveForm.errors.amountCents}</p>{/if}
        <p class="text-muted-foreground text-xs">Approving locks the amount; mark it paid from the decision log once the money is out.</p>
      </form>

      <form class="grid gap-2 border-t pt-5" onsubmit={(e: SubmitEvent) => { e.preventDefault(); reject(reviewing!) }}>
        <Label for="refund-note">Or reject with a note</Label>
        <Textarea id="refund-note" bind:value={rejectForm.note} placeholder="Why this refund is rejected" maxlength="200" rows={2} />
        {#if rejectForm.errors.note}<p class="text-destructive text-sm" role="alert">{rejectForm.errors.note}</p>{/if}
        <Button type="submit" variant="outline" class="hover:text-destructive w-fit" disabled={rejectForm.processing || !rejectForm.note.trim()}>
          {#if rejectForm.processing}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<ThumbsDown class="size-4" aria-hidden="true" />{/if} Reject refund
        </Button>
      </form>
    </div>
  {/if}
  {#snippet footer()}
    <Button variant="ghost" onclick={close}>Cancel</Button>
    <Button type="submit" form="approve-refund" disabled={approveForm.processing || approveForm.amountCents <= 0}>
      {#if approveForm.processing}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<Check class="size-4" aria-hidden="true" />{/if}
      Approve {money(approveForm.amountCents)}
    </Button>
  {/snippet}
</SidePanel>
