<script lang="ts">
  import { useForm } from '@inertiajs/svelte'
  import { FileUp, Loader2, ShieldCheck } from '@lucide/svelte'
  import DataTable from '@/components/DataTable.svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import PriceTag from '@/components/PriceTag.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import Select from '@/components/ui/select/Select.svelte'
  import Textarea from '@/components/ui/textarea/Textarea.svelte'
  import { money } from '@/lib/money'
  import { fmtDate, fmtDateTime } from '@/lib/time'
  import type { ClaimRow, PolicyRow } from '@/types'

  /** GET /account/insurance — Spot Cover policies + claims; POST /account/insurance/{policyId}/claims files a claim. */
  let {
    policies,
    claims,
  }: {
    policies: PolicyRow[]
    claims: ClaimRow[]
  } = $props()

  const kindLabel: Record<string, string> = { damage: 'Damage', theft: 'Theft', brokenwindow: 'Broken window', other: 'Other' }
  const claimable = $derived(policies.filter(p => p.status === 'active'))

  const claimForm = useForm({
    policyId: claimable[0]?.id ?? '',
    kind: 'damage',
    description: '',
    claimedCents: '',
    incidentOn: new Date().toISOString().slice(0, 10),
  })

  const selectedPolicy = $derived(policies.find(p => p.id === claimForm.policyId))
  const maxClaim = $derived(selectedPolicy?.coverageCents ?? 0)

  const fileClaim = () =>
    claimForm.post(`/account/insurance/${claimForm.policyId}/claims`, {
      onError: () => claimForm.reset('description'),
    })
</script>

<PageTitle title="Spot Cover" />

{#snippet premiumCell(p: PolicyRow)}<PriceTag cents={p.premiumCents} />{/snippet}
{#snippet coverageCell(p: PolicyRow)}<PriceTag cents={p.coverageCents} />{/snippet}
{#snippet statusCell(p: PolicyRow)}<StatusBadge status={p.status} size="xs" />{/snippet}
{#snippet claimedCell(c: ClaimRow)}<PriceTag cents={c.claimedCents} />{/snippet}
{#snippet payoutCell(c: ClaimRow)}{#if c.payoutCents !== null}<PriceTag cents={c.payoutCents} />{:else}—{/if}{/snippet}
{#snippet claimStatusCell(c: ClaimRow)}<StatusBadge status={c.status} size="xs" />{/snippet}

<div class="flex flex-col gap-6">
  <PageHeader title="Spot Cover" description="Damage and theft coverage bundled with your bookings." />

  {#if policies.length}
    <div class="bg-card shadow-card overflow-hidden rounded-2xl border">
      <DataTable
        rows={policies}
        rowKey={(p) => p.id}
        rowHref={(p) => `/account/bookings/${p.reservationId ?? ''}`}
        columns={[
          { key: 'lot', header: 'Lot', primary: true, cell: (p: PolicyRow) => p.lotName ?? 'Booking' },
          { key: 'span', header: 'Covered through', cell: (p: PolicyRow) => (p.reservedThrough ? fmtDate(p.reservedThrough, true) : fmtDate(p.startedAt, true)) },
          { key: 'premium', header: 'Premium', align: 'right' as const, cell: (p: PolicyRow) => premiumCell(p) },
          { key: 'coverage', header: 'Coverage', align: 'right' as const, desktopOnly: true, cell: (p: PolicyRow) => coverageCell(p) },
          { key: 'status', header: 'Status', align: 'right' as const, cell: (p: PolicyRow) => statusCell(p) },
        ]}
      />
    </div>
  {:else}
    <div class="bg-card shadow-card rounded-3xl border">
      <EmptyState icon={ShieldCheck} title="No policies yet" description="Add Spot Cover at checkout — 12% of parking, minimum $2, covering damage or theft up to the lot's limit." class="py-14">
        <Button href="/lots" class="rounded-2xl">Find a deck</Button>
      </EmptyState>
    </div>
  {/if}

  {#if claims.length}
    <section class="bg-card shadow-card overflow-hidden rounded-2xl border">
      <h2 class="font-display px-5 pt-5 pb-3 text-base font-semibold">Claims</h2>
      <div class="border-t">
        <DataTable
          rows={claims}
          rowKey={(c) => c.id}
          columns={[
            { key: 'kind', header: 'Type', primary: true, cell: (c: ClaimRow) => kindLabel[c.kind] ?? c.kind },
            { key: 'filed', header: 'Filed', cell: (c: ClaimRow) => fmtDateTime(c.createdAt) },
            { key: 'claimed', header: 'Claimed', align: 'right' as const, cell: (c: ClaimRow) => claimedCell(c) },
            { key: 'payout', header: 'Payout', align: 'right' as const, cell: (c: ClaimRow) => payoutCell(c) },
            { key: 'status', header: 'Status', align: 'right' as const, cell: (c: ClaimRow) => claimStatusCell(c) },
          ]}
        />
      </div>
    </section>
  {/if}

  {#if claimable.length}
    <section class="bg-card shadow-card rounded-3xl border p-5 sm:p-6">
      <h2 class="font-display flex items-center gap-2 text-base font-semibold">
        <FileUp class="size-4" aria-hidden="true" /> File a claim
      </h2>
      <form novalidate class="mt-4 grid gap-4 sm:grid-cols-2" onsubmit={(event: SubmitEvent) => { event.preventDefault(); fileClaim() }}>
        <div class="grid gap-2">
          <Label for="policy">Policy</Label>
          <Select
            id="policy"
            items={claimable.map(p => ({ value: p.id, label: `${p.lotName ?? 'Booking'} — ${money(p.coverageCents)} cover` }))}
            bind:value={claimForm.policyId}
            ariaLabel="Policy"
            class="w-full"
          />
        </div>
        <div class="grid gap-2">
          <Label for="kind">Claim type</Label>
          <Select
            id="kind"
            items={[
              { value: 'damage', label: 'Damage' },
              { value: 'theft', label: 'Theft' },
              { value: 'brokenwindow', label: 'Broken window' },
              { value: 'other', label: 'Other' },
            ]}
            bind:value={claimForm.kind}
            ariaLabel="Claim type"
            class="w-full"
          />
          {#if claimForm.errors.kind}<p class="text-destructive text-sm" role="alert">{claimForm.errors.kind}</p>{/if}
        </div>
        <div class="grid gap-2">
          <Label for="incidentOn">Incident date</Label>
          <Input id="incidentOn" type="date" bind:value={claimForm.incidentOn} max={new Date().toISOString().slice(0, 10)} required />
        </div>
        <div class="grid gap-2">
          <Label for="claimedCents">Amount claimed{maxClaim ? ` (up to ${money(maxClaim)})` : ''}</Label>
          <Input id="claimedCents" type="number" min="1" step="1" bind:value={claimForm.claimedCents} placeholder="Cents" required />
          {#if claimForm.errors.claimedCents}<p class="text-destructive text-sm" role="alert">{claimForm.errors.claimedCents}</p>{/if}
        </div>
        <div class="grid gap-2 sm:col-span-2">
          <Label for="description">What happened</Label>
          <Textarea id="description" bind:value={claimForm.description} rows={3} maxlength="500" required placeholder="Describe the damage or theft, and where in the deck it happened." />
          {#if claimForm.errors.description}<p class="text-destructive text-sm" role="alert">{claimForm.errors.description}</p>{/if}
        </div>
        <div class="sm:col-span-2">
          <Button type="submit" size="lg" class="rounded-2xl" disabled={claimForm.processing}>
            {#if claimForm.processing}<Loader2 class="animate-spin" aria-hidden="true" />{/if}
            {claimForm.processing ? 'Filing…' : 'Submit claim'}
          </Button>
        </div>
      </form>
    </section>
  {/if}
</div>
