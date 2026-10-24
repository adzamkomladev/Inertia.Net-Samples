<script lang="ts">
  import { untrack } from 'svelte'
  import { useForm } from '@inertiajs/svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import MoneyInput from '@/components/MoneyInput.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import Textarea from '@/components/ui/textarea/Textarea.svelte'
  import { money } from '@/lib/money'
  import { fmtDateTime } from '@/lib/time'
  import type { ClaimRow } from '@/types'
  let { detail }: { detail: { claim: ClaimRow & { incidentOn: string; description: string }; reservationId: string; coverageCents: number; premiumCents: number; email: string | null; decidedAt: string | null; paidAt: string | null } } = $props()
  const form = useForm(untrack(() => ({ payoutCents: detail.claim.payoutCents ?? detail.claim.claimedCents, note: '' })))
  function decide(action: string) { form.post(`/admin/claims/${detail.claim.id}/${action}`, { preserveScroll: true }) }
</script>
<PageTitle title="Insurance claim details" />
<div class="space-y-6">
<a href="/admin/insurance" class="text-primary hover:underline">&larr; Spot Cover claims</a>
<PageHeader title="Insurance claim details" description={`${detail.claim.kind} · ${detail.claim.id}`} />
<div class="grid gap-6 lg:grid-cols-[1fr_22rem]">
<section class="bg-card space-y-5 rounded-xl border p-6">
<StatusBadge status={detail.claim.status} />
<h2 class="font-semibold">Incident report</h2>
<p class="whitespace-pre-wrap text-sm leading-7">{detail.claim.description}</p>
<dl class="grid gap-4 text-sm sm:grid-cols-2">
<div>
<dt class="text-muted-foreground">Incident date</dt>
<dd>{fmtDateTime(detail.claim.incidentOn)}</dd>
</div>
<div>
<dt class="text-muted-foreground">Submitted</dt>
<dd>{fmtDateTime(detail.claim.createdAt)}</dd>
</div>
<div>
<dt class="text-muted-foreground">Claimed</dt>
<dd>{money(detail.claim.claimedCents)}</dd>
</div>
<div>
<dt class="text-muted-foreground">Coverage limit</dt>
<dd>{money(detail.coverageCents)}</dd>
</div>
<div>
<dt class="text-muted-foreground">Policy premium</dt>
<dd>{money(detail.premiumCents)}</dd>
</div>
<div>
<dt class="text-muted-foreground">Approved payout</dt>
<dd>{detail.claim.payoutCents == null ? 'Pending' : money(detail.claim.payoutCents)}</dd>
</div>
{#if detail.decidedAt}<div>
<dt class="text-muted-foreground">Decided</dt>
<dd>{fmtDateTime(detail.decidedAt)}</dd>
</div>{/if}{#if detail.paidAt}<div>
<dt class="text-muted-foreground">Paid</dt>
<dd>{fmtDateTime(detail.paidAt)}</dd>
</div>{/if}</dl>
{#if detail.claim.decisionNote}<p class="rounded-lg bg-muted p-4 text-sm">{detail.claim.decisionNote}</p>{/if}<a href={`/admin/bookings/${detail.reservationId}`} class="text-sm text-primary underline">View booking</a>
</section>
<aside class="space-y-6">
<section class="bg-card space-y-2 rounded-xl border p-6">
<h2 class="font-semibold">Customer</h2>
<p>{detail.claim.userName}</p>
<p class="break-all text-sm text-muted-foreground">{detail.email}</p>
</section>
{#if detail.claim.status === 'submitted' || detail.claim.status === 'approved'}<form class="bg-card space-y-4 rounded-xl border p-6" onsubmit={(event) => event.preventDefault()}>
<h2 class="font-semibold">Review claim</h2>
<div class="space-y-2">
<Label for="claim-payout">Payout (up to {money(detail.coverageCents)})</Label>
<MoneyInput id="claim-payout" bind:cents={form.payoutCents} min={1} max={detail.coverageCents} invalid={!!form.errors.payoutCents} />
</div>
<div class="space-y-2">
<Label for="claim-note">Decision note</Label>
<Textarea id="claim-note" bind:value={form.note} rows={3} maxlength={800} />
</div>
{#each Object.values(form.errors) as error}<p class="text-sm text-destructive" role="alert">{error}</p>{/each}<div class="flex flex-wrap gap-2">
{#if detail.claim.status === 'submitted'}<Button disabled={form.processing} onclick={() => decide('approve')}>Approve</Button>
<Button variant="destructive" disabled={form.processing} onclick={() => decide('reject')}>Reject</Button>{:else}<Button disabled={form.processing} onclick={() => decide('pay')}>Record payout</Button>{/if}</div>
</form>{/if}</aside>
</div>
</div>
