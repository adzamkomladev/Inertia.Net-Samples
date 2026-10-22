<script lang="ts">
  import { untrack } from 'svelte'
  import SidePanel from '@/components/SidePanel.svelte'
  import SearchField from '@/components/SearchField.svelte'
  import { useForm, usePage } from '@inertiajs/svelte'
  import Loader2 from '@lucide/svelte/icons/loader-circle'
  import ScrollText from '@lucide/svelte/icons/scroll-text'
  import Siren from '@lucide/svelte/icons/siren'
  import ShieldAlert from '@lucide/svelte/icons/shield-alert'
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
  const kindItems = $derived(kinds.map(kind => ({ value: kind, label: kind.replace(/_/g, ' ').replace(/^./, c => c.toUpperCase()) })))

  const applyFilter = (patch: Record<string, string>) =>
    router.get(isAdmin ? '/admin/incidents' : '/guard/incidents', { lot: filterLot ?? '', status: filterStatus ?? '', ...patch }, { preserveState: true, preserveScroll: true })

  const reportForm = useForm(untrack(() => ({
    lotId: filterLot ?? lots[0]?.id ?? '',
    kind: kinds[0] ?? 'other',
    plateNo: '',
    description: '',
  })))
  let reportOpen = $state(false)
  let q = $state('')
  const filtered = $derived(incidents.filter(i => `${i.lotName} ${i.plateNo ?? ''} ${i.description} ${i.kind}`.toLowerCase().includes(q.trim().toLowerCase())))
  const report = () => reportForm.post('/guard/incidents', { onSuccess: () => { reportOpen = false; reportForm.reset() } })

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
  <PageHeader title="Incidents" description="Keep a clear record of what happens on your lots.">
    {#snippet actions()}{#if !isAdmin}<Button onclick={() => reportOpen = true}>Report incident</Button>{/if}{/snippet}
  </PageHeader>
  <SearchField bind:value={q} label="Search incidents" placeholder="Search description, lot, plate or kind…" />
  <div class="flex flex-col gap-3 sm:flex-row">
    <Select items={[{ value: '', label: 'All lots' }, ...lots.map(l => ({ value: l.id, label: l.name }))]} value={filterLot ?? ''} onValueChange={(v) => applyFilter({ lot: v })} ariaLabel="Filter by lot" class="sm:w-48" />
    <Select items={statusItems} value={filterStatus ?? ''} onValueChange={(v) => applyFilter({ status: v })} ariaLabel="Filter by status" class="sm:w-40" />
  </div>

  {#if filtered.length}
    <div class="bg-card shadow-card overflow-hidden rounded-xl">
      <DataTable
        rows={filtered}
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
      <EmptyState icon={ShieldAlert} title="No incidents" description="Try another search or filter, or report a new incident." class="py-12" />
    </div>
  {/if}

  <!-- Report incident (guard console only; the admin variant is read-only) -->
  {#if !isAdmin}
    <SidePanel bind:open={reportOpen} title="Report incident" description="Record the facts, location and people involved. Your report will be visible to administrators.">
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
    </SidePanel>
  {/if}
</div>
