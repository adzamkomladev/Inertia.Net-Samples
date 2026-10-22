<script lang="ts">
  import { useForm, usePage } from '@inertiajs/svelte'
  import { Loader2, ScrollText, Siren, ShieldAlert } from '@lucide/svelte'
  import DataTable from '@/components/DataTable.svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import Select from '@/components/ui/select/Select.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import Textarea from '@/components/ui/textarea/Textarea.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import { router } from '@inertiajs/svelte'
  import { fmtDateTime } from '@/lib/time'
  import type { LotOption } from '@/types'

  /** GET/POST /guard/incidents — deck incident log with report + resolve flows. */
  let {
    lots,
    filterLot,
    filterStatus,
    incidents,
    kinds,
  }: {
    lots: LotOption[]
    filterLot: string | null
    filterStatus: string | null
    incidents: {
      id: string
      lotId: string
      kind: string
      plateNo: string | null
      description: string
      note: string | null
      status: string
      createdAt: string
      resolvedAt: string | null
      lotName: string | null
      reporterName: string | null
    }[]
    kinds: string[]
  } = $props()

  // The backend renders guard/Incidents and admin/Incidents with the same props; the admin
  // variant is read-only (no create/resolve endpoints on /admin/*).
  const page = usePage()
  const isAdmin = $derived(page.component === 'admin/Incidents')

  const statusItems = [
    { value: '', label: 'Any status' },
    { value: 'open', label: 'Open' },
    { value: 'resolved', label: 'Resolved' },
  ]
  const kindItems = kinds.map(kind => ({ value: kind, label: kind.replace(/_/g, ' ').replace(/^./, c => c.toUpperCase()) }))

  const applyFilter = (patch: Record<string, string>) =>
    router.get(isAdmin ? '/admin/incidents' : '/guard/incidents', { lot: filterLot ?? '', status: filterStatus ?? '', ...patch }, { preserveState: true, preserveScroll: true })

  const reportForm = useForm({
    lotId: filterLot ?? lots[0]?.id ?? '',
    kind: kinds[0] ?? 'other',
    plateNo: '',
    description: '',
  })
  const report = () => reportForm.post('/guard/incidents')

  const resolve = (id: string) => router.post(`/guard/incidents/${id}/resolve`, {})
</script>

<PageTitle title="Incidents" />

{#snippet statusCell(i: (typeof incidents)[number])}<StatusBadge status={i.status} size="xs" />{/snippet}
{#snippet kindCell(i: (typeof incidents)[number])}<span class="capitalize">{i.kind.replace(/_/g, ' ')}</span>{/snippet}
{#snippet whenCell(i: (typeof incidents)[number])}<span class="font-mono text-xs tabular-nums">{fmtDateTime(i.createdAt)}</span>{/snippet}
{#snippet actionCell(i: (typeof incidents)[number])}
  {#if i.status === 'open' && !isAdmin}
    <button
      type="button"
      onclick={() => resolve(i.id)}
      class="text-destructive hover:bg-destructive/10 rounded-lg px-2 py-1 text-xs font-medium transition-colors"
    >Resolve</button>
  {:else}
    <span class="text-muted-foreground text-xs">{i.resolvedAt ? fmtDateTime(i.resolvedAt) : ''}</span>
  {/if}
{/snippet}

<div class="flex flex-col gap-6">
  <PageHeader title="Incidents" description="Unauthorized entries, theft, damage and patrol notes across decks." />

  {#if incidents.length}
    <div class="bg-card shadow-card overflow-hidden rounded-xl">
      <div class="flex flex-col gap-2 border-b p-3 sm:flex-row sm:items-center">
        <Select items={lots.map(l => ({ value: l.id, label: l.name }))} value={filterLot ?? ''} onValueChange={(v) => applyFilter({ lot: v })} ariaLabel="Filter by lot" placeholder="All decks" class="sm:w-44" />
        <Select items={statusItems} value={filterStatus ?? ''} onValueChange={(v) => applyFilter({ status: v })} ariaLabel="Filter by status" placeholder="Any status" class="sm:w-36" />
        <span class="text-muted-foreground ml-auto hidden text-xs sm:block">{incidents.length} incident{incidents.length === 1 ? '' : 's'}</span>
      </div>
      <DataTable
        rows={incidents}
        rowKey={(i) => i.id}
        columns={[
          { key: 'kind', header: 'Kind', primary: true, snippet: kindCell },
          { key: 'lot', header: 'Deck', cell: (i) => i.lotName ?? '—' },
          { key: 'plate', header: 'Plate', desktopOnly: true, cell: (i) => i.plateNo ?? '—' },
          { key: 'description', header: 'Description', desktopOnly: true, cell: (i) => i.description.length > 80 ? `${i.description.slice(0, 80)}…` : i.description },
          { key: 'reporter', header: 'Reported by', desktopOnly: true, cell: (i) => i.reporterName ?? 'Guard' },
          { key: 'when', header: 'When', snippet: whenCell },
          { key: 'status', header: 'Status', align: 'right' as const, snippet: statusCell },
          { key: 'action', header: '', align: 'right' as const, snippet: actionCell },
        ]}
      />
    </div>
  {:else}
    <div class="bg-card shadow-card rounded-xl">
      <EmptyState icon={ShieldAlert} title="No incidents" description="The decks are quiet. Report anything suspicious with the form below." class="py-12" />
    </div>
  {/if}

  <!-- Report incident (guard console only; the admin variant is read-only) -->
  {#if !isAdmin}
    <section class="bg-card shadow-card rounded-xl p-5 sm:p-6">
      <h2 class="font-display flex items-center gap-2 text-base font-semibold">
        <Siren class="text-destructive size-4" aria-hidden="true" /> Report an incident
      </h2>
      <form novalidate class="mt-4 grid gap-4 sm:grid-cols-2" onsubmit={(event: SubmitEvent) => { event.preventDefault(); report() }}>
        <div class="grid gap-2">
          <Label for="lot">Deck</Label>
          <Select id="lot" items={lots.map(l => ({ value: l.id, label: l.name }))} bind:value={reportForm.lotId} ariaLabel="Lot" class="w-full" />
          {#if reportForm.errors.lotId}<p class="text-destructive text-sm" role="alert">{reportForm.errors.lotId}</p>{/if}
        </div>
        <div class="grid gap-2">
          <Label for="kind">Kind</Label>
          <Select id="kind" items={kindItems} bind:value={reportForm.kind} ariaLabel="Incident kind" class="w-full" />
          {#if reportForm.errors.kind}<p class="text-destructive text-sm" role="alert">{reportForm.errors.kind}</p>{/if}
        </div>
        <div class="grid gap-2">
          <Label for="plate">Plate (if involved)</Label>
          <Input id="plate" bind:value={reportForm.plateNo} maxlength="16" placeholder="Optional" />
        </div>
        <div class="grid gap-2 sm:col-span-2">
          <Label for="description">What happened</Label>
          <Textarea id="description" bind:value={reportForm.description} rows={3} maxlength="500" required placeholder="Facts, location in the deck, involved parties." />
          {#if reportForm.errors.description}<p class="text-destructive text-sm" role="alert">{reportForm.errors.description}</p>{/if}
        </div>
        <div>
          <Button type="submit"  disabled={reportForm.processing}>
            {#if reportForm.processing}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<ScrollText class="size-4" aria-hidden="true" />{/if}
            {reportForm.processing ? 'Reporting…' : 'File report'}
          </Button>
        </div>
      </form>
    </section>
  {/if}
</div>
