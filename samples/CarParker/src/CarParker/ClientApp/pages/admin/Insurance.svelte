<script lang="ts">
  import { claimKindLabel } from '@/lib/labels'
  import { Link, useForm } from '@inertiajs/svelte'
  import Check from '@lucide/svelte/icons/check'
  import ChevronRight from '@lucide/svelte/icons/chevron-right'
  import ShieldCheck from '@lucide/svelte/icons/shield-check'
  import ThumbsDown from '@lucide/svelte/icons/thumbs-down'
  import EmptyState from '@/components/EmptyState.svelte'
  import Pagination from '@/components/Pagination.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import PriceTag from '@/components/PriceTag.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import ConfirmDialog from '@/components/ConfirmDialog.svelte'
  import { buttonVariants } from '@/components/ui/button/variants'
  import { money } from '@/lib/money'
  import { fmtDate } from '@/lib/time'
  import type { ClaimRow, PolicyRow } from '@/types'

  /** GET /admin/insurance — Spot Cover policies and the claim desk. */
  let {
    policies,
    claims,
    policiesPage = 1,
    policiesHasMore = false,
    claimsPage = 1,
    claimsHasMore = false,
  }: {
    policies: PolicyRow[]
    claims: ClaimRow[]
    policiesPage: number
    policiesHasMore: boolean
    claimsPage: number
    claimsHasMore: boolean
  } = $props()

  // Each claim keeps its own note; only the claim being decided sends it.
  let notes = $state<Record<string, string>>({})
  let deciding = $state<string | null>(null)
  const decideForm = useForm({ payoutCents: 0, note: '' })
  const decide = (claim: ClaimRow, decision: 'approve' | 'pay' | 'reject') => {
    // One decision at a time: a second visit would cancel the first mid-flight.
    if (decideForm.processing) return
    decideForm.payoutCents = claim.claimedCents
    decideForm.note = decision === 'pay' ? '' : (notes[claim.id] ?? '').trim()
    deciding = claim.id
    decideForm.post(`/admin/claims/${claim.id}/${decision}`, {
      preserveScroll: true,
      onSuccess: () => { delete notes[claim.id] },
      onFinish: () => { deciding = null; decideForm.note = '' },
    })
  }
  /** The server sends enum names lowercased: "underreview". */
  const waiting = (claim: ClaimRow) => claim.status === 'submitted' || claim.status === 'underreview'

  const payout = (claim: ClaimRow) => claim.payoutCents ?? claim.claimedCents
</script>

<PageTitle title="Spot Cover" />

<div class="flex flex-col gap-8">
  <PageHeader title="Spot Cover" description="Security insurance policies sold with bookings, and the claim desk." />

  <section class="flex flex-col gap-3">
    <h2 class="font-display flex items-center gap-2 text-base font-semibold">
      <ShieldCheck class="size-4" aria-hidden="true" /> Claims
      <span class="bg-cta/25 text-cta-ink ml-1 rounded-full px-2 py-0.5 text-xs font-semibold">
        {claims.filter(waiting).length} waiting
      </span>
    </h2>
    {#if claims.length}
      <div class="flex flex-col gap-3">
        {#each claims as claim (claim.id)}
          <div class="bg-card shadow-card flex flex-col gap-3 rounded-xl p-4 lg:flex-row lg:items-center">
            <div class="min-w-0 flex-1">
              <div class="flex flex-wrap items-center gap-2">
                <span class="text-sm font-semibold">{claimKindLabel(claim.kind)}</span>
                <StatusBadge status={claim.status} size="xs" />
                <PriceTag cents={claim.claimedCents} />
                {#if claim.payoutCents != null}<span class="text-muted-foreground text-xs">payout {money(claim.payoutCents)}</span>{/if}
              </div>
              <p class="text-muted-foreground mt-1 truncate text-xs">
                {claim.userName ?? 'Customer'} · incident {claim.incidentOn ? fmtDate(claim.incidentOn, false) : '—'} · filed {fmtDate(claim.createdAt, true)}
              </p>
              {#if claim.description}<p class="mt-1 text-sm">{claim.description}</p>{/if}
              {#if claim.decisionNote}<p class="text-muted-foreground mt-1 text-xs">Decision: {claim.decisionNote}</p>{/if}
              <Link href={`/admin/claims/${claim.id}`} prefetch="hover" cacheFor="30s" class="group mt-3 inline-flex min-h-11 items-center rounded-full border px-3 text-xs font-medium text-muted-foreground transition hover:bg-accent hover:text-foreground md:min-h-9">View claim details <ChevronRight class="ml-1 size-3.5 transition-transform group-hover:translate-x-0.5" aria-hidden="true" /></Link>
            </div>
            {#if waiting(claim)}
              <div class="flex flex-wrap items-center gap-2">
                <label for={`decision-note-${claim.id}`} class="sr-only">Decision note for {claim.userName ?? 'this customer'}'s claim (optional)</label>
                <input
                  id={`decision-note-${claim.id}`}
                  class="border-input bg-background min-h-11 w-44 rounded-full border px-3 py-1.5 text-sm md:min-h-9"
                  placeholder="Decision note"
                  maxlength={200}
                  bind:value={notes[claim.id]}
                />
                <Button size="sm" onclick={() => decide(claim, 'approve')} disabled={decideForm.processing} aria-busy={deciding === claim.id}>
                  <Check class="size-3.5" aria-hidden="true" /> Approve
                </Button>
                <ConfirmDialog
                  title="Reject this claim?"
                  description={`${claim.userName ?? 'The customer'} is told it was rejected${notes[claim.id]?.trim() ? ', with your note' : ''}. This cannot be undone.`}
                  confirmLabel="Reject claim"
                  destructive
                  onConfirm={() => decide(claim, 'reject')}
                  triggerClass={buttonVariants({ variant: 'destructive', size: 'sm' })}
                  disabled={decideForm.processing}
                >
                  {#snippet trigger()}<ThumbsDown class="size-3.5" aria-hidden="true" /> Reject{/snippet}
                </ConfirmDialog>
              </div>
            {:else if claim.status === 'approved'}
              <Button size="sm" onclick={() => decide(claim, 'pay')} disabled={decideForm.processing}>Pay {money(payout(claim))}</Button>
            {/if}
          </div>
        {/each}
      </div>
    {:else}
      <EmptyState icon={ShieldCheck} title="No claims" description="Spot Cover claims arrive here for review." />
    {/if}
    {#if claimsPage > 1 || claimsHasMore}<Pagination href="/admin/insurance" page={claimsPage} hasMore={claimsHasMore} pageParam="claimsPage" params={{ policiesPage }} />{/if}
  </section>

  <section class="flex flex-col gap-3">
    <h2 class="font-display text-base font-semibold">Active & past policies</h2>
    {#if policies.length}
      <div class="bg-card shadow-card overflow-hidden rounded-xl">
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
    {#if policiesPage > 1 || policiesHasMore}<Pagination href="/admin/insurance" page={policiesPage} hasMore={policiesHasMore} pageParam="policiesPage" params={{ claimsPage }} />{/if}
  </section>
</div>
