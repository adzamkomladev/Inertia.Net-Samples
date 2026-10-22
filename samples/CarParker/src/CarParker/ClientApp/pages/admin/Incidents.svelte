<script lang="ts">
  import { useForm } from '@inertiajs/svelte'
  import { ShieldAlert } from '@lucide/svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import Select from '@/components/ui/select/Select.svelte'
  import { router } from '@inertiajs/svelte'
  import { fmtDate } from '@/lib/time'
  import type { LotOption } from '@/types'

  /** GET /admin/incidents?lot=&status= — the security log across decks. */
  let {
    lots,
    filterLot,
    filterStatus,
    incidents,
  }: {
    lots: LotOption[]
    filterLot: string | null
    filterStatus: string | null
    incidents: {
      id: string
      lotId: string
      kind: string
      plateNo: string | null
      description: string | null
      note: string | null
      status: string
      createdAt: string
      resolvedAt: string | null
      lotName: string | null
      reporterName: string | null
    }[]
  } = $props()

  const resolveForm = useForm({ note: '' })
  const filter = (params: { lot?: string; status?: string }) =>
    router.get('/admin/incidents', { lot: filterLot ?? undefined, status: filterStatus ?? undefined, ...params }, { preserveScroll: true })
  const resolve = (id: string) => resolveForm.post(`/admin/incidents/${id}/resolve`, { onFinish: () => (resolveForm.note = '') })
</script>

<PageTitle title="Incidents" />

<div class="flex flex-col gap-6">
  <PageHeader title="Incidents" description="Security events reported from the guard consoles." />

  <div class="grid gap-3 sm:grid-cols-2 sm:max-w-md">
    <Select
      items={[{ value: '', label: 'All lots' }, ...lots.map((l) => ({ value: l.id, label: l.name }))]}
      value={filterLot ?? ''}
      onValueChange={(v: string) => filter({ lot: v })}
      ariaLabel="Filter by lot"
      placeholder="All lots"
    />
    <Select
      items={[{ value: '', label: 'All statuses' }, { value: 'open', label: 'Open' }, { value: 'resolved', label: 'Resolved' }]}
      value={filterStatus ?? ''}
      onValueChange={(v: string) => filter({ status: v })}
      ariaLabel="Filter by status"
      placeholder="All statuses"
    />
  </div>

  {#if incidents.length}
    <div class="flex flex-col gap-3">
      {#each incidents as incident (incident.id)}
        <div class="bg-card shadow-card flex flex-col gap-3 rounded-xl p-4 lg:flex-row lg:items-start">
          <div class="min-w-0 flex-1">
            <div class="flex flex-wrap items-center gap-2">
              <StatusBadge status={incident.status} size="xs" />
              <span class="text-sm font-semibold capitalize">{incident.kind.replace('_', ' ')}</span>
              {#if incident.plateNo}
                <span class="bg-primary/10 text-primary rounded-xl px-2 py-0.5 font-mono text-xs font-semibold">{incident.plateNo.toUpperCase()}</span>
              {/if}
            </div>
            <p class="text-muted-foreground mt-1 text-xs">
              {incident.lotName ?? 'Unknown lot'} · {incident.reporterName ?? 'Unknown reporter'} · {fmtDate(incident.createdAt, true)}
            </p>
            {#if incident.description}<p class="mt-1 text-sm">{incident.description}</p>{/if}
            {#if incident.note}<p class="text-muted-foreground mt-1 text-xs">Resolution: {incident.note}</p>{/if}
          </div>
          {#if incident.status === 'open'}
            <form
              class="flex items-center gap-2"
              onsubmit={(e: SubmitEvent) => { e.preventDefault(); resolve(incident.id) }}
            >
              <input
                class="border-input bg-background w-44 rounded-full border px-3 py-1.5 text-sm"
                placeholder="Resolution note"
                maxlength={200}
                bind:value={resolveForm.note}
              />
              <button
                type="submit"
                class="bg-primary text-primary-foreground hover:bg-primary/90 rounded-full px-3.5 py-1.5 text-sm font-medium disabled:opacity-50"
                disabled={resolveForm.processing}
              >Resolve</button>
            </form>
          {/if}
        </div>
      {/each}
    </div>
  {:else}
    <EmptyState icon={ShieldAlert} title="Nothing to report" description="Guard consoles file incidents here — unauthorized entries, damage, patrol notes." />
  {/if}
</div>
