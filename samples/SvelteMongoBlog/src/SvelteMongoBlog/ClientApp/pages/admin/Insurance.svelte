<script lang="ts">
  import { useForm } from '@inertiajs/svelte'
  import { Check, ShieldCheck, ThumbsDown } from '@lucide/svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import PriceTag from '@/components/PriceTag.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import { money } from '@/lib/money'
  import { fmtDate } from '@/lib/time'
  import type { ClaimRow, PolicyRow } from '@/types'

  /** GET /admin/insurance — Spot Cover policies and the claim desk. */
  let {
    policies,
    claims,
  }: {
    policies: PolicyRow[]
    claims: ClaimRow[]
  } = $props()

  const decideForm = useForm({ decision: 'approve', note: '' })
  const decide = (claim: ClaimRow, decision: 'approve' | 'pay' | 'reject') => {
    decideForm.decision = decision
    decideForm.post(`/admin/claims/${claim.id}/${decision}`, {
      onFinish: () => (decideForm.note = ''),
    })
  }

  const payout = (claim: ClaimRow) => claim.payoutCents ?? claim.claimedCents
</script>

<PageTitle title="Spot Cover" />

<div class="flex flex-col gap-8">
  <PageHeader title="Spot Cover" description="Security insurance policies sold with bookings, and the claim desk." />

  <section class="flex flex-col gap-3">
    <h2 class="font-display flex items-center gap-2 text-base font-semibold">
      <ShieldCheck class="size-4" aria-hidden="true" /> Claims
      <span class="bg-amber-500/15 text-amber-700 dark:text-amber-400 ml-1 rounded-full px-2 py-0.5 text-xs font-semibold">
        {claims.filter((c) => c.status === 'submitted' || c.status === 'under_review').length} waiting
      </span>
    </h2>
    {#if claims.length}
      <div class="flex flex-col gap-3">
        {#each claims as claim (claim.id)}
          <div class="bg-card shadow-card flex flex-col gap-3 rounded-2xl border p-4 lg:flex-row lg:items-center">
            <div class="min-w-0 flex-1">
              <div class="flex flex-wrap items-center gap-2">
                <span class="text-sm font-semibold capitalize">{claim.kind.replace('_', ' ')}</span>
                <StatusBadge status={claim.status} size="xs" />
                <PriceTag cents={claim.claimedCents} />
                {#if claim.payoutCents != null}<span class="text-muted-foreground text-xs">payout {money(claim.payoutCents)}</span>{/if}
              </div>
              <p class="text-muted-foreground mt-1 truncate text-xs">
                {claim.userName ?? 'Customer'} · incident {claim.incidentOn ? fmtDate(claim.incidentOn, false) : '—'} · filed {fmtDate(claim.createdAt, true)}
              </p>
              {#if claim.description}<p class="mt-1 text-sm">{claim.description}</p>{/if}
              {#if claim.decisionNote}<p class="text-muted-foreground mt-1 text-xs">Decision: {claim.decisionNote}</p>{/if}
            </div>
            {#if claim.status === 'submitted' || claim.status === 'under_review'}
              <div class="flex flex-wrap items-center gap-2">
                <input
                  class="border-input bg-background w-44 rounded-full border px-3 py-1.5 text-sm"
                  placeholder="Decision note"
                  maxlength={200}
                  bind:value={decideForm.note}
                />
                <Button size="sm" variant="outline" onclick={() => decide(claim, 'approve')} disabled={decideForm.processing}>
                  <Check class="size-3.5" aria-hidden="true" /> Approve
                </Button>
                <Button size="sm" onclick={() => decide(claim, 'pay')} disabled={decideForm.processing}>Pay {`$${(payout(claim) / 100).toFixed(2)}`}</Button>
                <Button size="sm" variant="destructive" onclick={() => decide(claim, 'reject')} disabled={decideForm.processing}>
                  <ThumbsDown class="size-3.5" aria-hidden="true" /> Reject
                </Button>
              </div>
            {/if}
          </div>
        {/each}
      </div>
    {:else}
      <EmptyState icon={ShieldCheck} title="No claims" description="Spot Cover claims arrive here for review." />
    {/if}
  </section>

  <section class="flex flex-col gap-3">
    <h2 class="font-display text-base font-semibold">Active & past policies</h2>
    {#if policies.length}
      <div class="bg-card shadow-card overflow-hidden rounded-2xl border">
        <ul class="divide-y">
          {#each policies as policy (policy.id)}
            <li class="flex flex-wrap items-center gap-3 p-4 text-sm">
              <StatusBadge status={policy.status} size="xs" />
              <span class="font-medium">{policy.lotName ?? '—'}</span>
              <span class="text-muted-foreground">{policy.userName ?? policy.userEmail ?? '—'}</span>
              <span class="text-muted-foreground ml-auto text-xs">premium {money(policy.premiumCents)} · covers {money(policy.coverageCents)} · from {fmtDate(policy.startedAt, false)}</span>
            </li>
          {/each}
        </ul>
      </div>
    {:else}
      <EmptyState icon={ShieldCheck} title="No policies" description="Policies appear as customers add Spot Cover at booking." />
    {/if}
  </section>
</div>
