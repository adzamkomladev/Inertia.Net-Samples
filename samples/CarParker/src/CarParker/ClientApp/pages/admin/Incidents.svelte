<script lang="ts">
  import { untrack } from 'svelte'
  import { useForm } from '@inertiajs/svelte'
  import SearchField from '@/components/SearchField.svelte'
  import Pagination from '@/components/Pagination.svelte'
  import { useDebouncedSearch } from '@/lib/debounced-search.svelte'
  import ShieldAlert from '@lucide/svelte/icons/shield-alert'
  import EmptyState from '@/components/EmptyState.svelte'
  import NotePopover from '@/components/NotePopover.svelte'
  import CheckCircle2 from '@lucide/svelte/icons/circle-check'
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
    query = '',
    incidents,
    page = 1,
    hasMore = false,
  }: {
    lots: LotOption[]
    filterLot: string | null
    filterStatus: string | null
    query: string
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
    page: number
    hasMore: boolean
  } = $props()

  let q = $state(untrack(() => query))
  $effect.pre(() => { q = query })
  useDebouncedSearch(() => q, '/admin/incidents', () => ({ lot: filterLot ?? '', status: filterStatus ?? '' }))
  const resolveForm = useForm({ note: '' })
  let resolving = $state<string | null>(null)
  const filter = (params: { lot?: string; status?: string }) =>
    router.get('/admin/incidents', { lot: filterLot ?? undefined, status: filterStatus ?? undefined, q, ...params }, { preserveScroll: true })
  const resolve = (id: string, note: string, close: () => void) => {
    resolving = id
    resolveForm.transform(() => ({ note })).post(`/admin/incidents/${id}/resolve`, {
      preserveScroll: true,
      onSuccess: close,
      onFinish: () => (resolving = null),
    })
  }
</script>

<PageTitle title="Incidents" />

<div class="flex flex-col gap-6">
  <PageHeader title="Incidents" description="Security events reported from the guard consoles." />

  <div class="flex flex-col gap-3 sm:flex-row">
    <SearchField bind:value={q} label="Search incidents" placeholder="Search description, lot, plate or kind…" />
    <Select
      items={[{ value: '', label: 'All lots' }, ...lots.map((l) => ({ value: l.id, label: l.name }))]}
      value={filterLot ?? ''}
      onValueChange={(v: string) => filter({ lot: v })}
      ariaLabel="Filter by lot"
      placeholder="All lots"
      class="sm:w-52"
    />
    <Select
      items={[{ value: '', label: 'All statuses' }, { value: 'open', label: 'Open' }, { value: 'resolved', label: 'Resolved' }]}
      value={filterStatus ?? ''}
      onValueChange={(v: string) => filter({ status: v })}
      ariaLabel="Filter by status"
      placeholder="All statuses"
      class="sm:w-44"
    />
  </div>

  {#if incidents.length}
    <div class="flex flex-col gap-3">
      {#each incidents as incident (incident.id)}
        <div class={['bg-card shadow-card animate-rise flex flex-col gap-3 rounded-xl border-l-4 p-4 lg:flex-row lg:items-start', incident.status === 'open' ? 'border-l-destructive' : 'border-l-transparent']}>
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
            {#if incident.note}<p class="bg-muted text-muted-foreground mt-2 w-fit rounded-lg px-2.5 py-1.5 text-xs"><span class="text-foreground font-semibold">Resolution</span> · {incident.note}</p>{/if}
          </div>
          {#if incident.status === 'open'}
            <NotePopover
              title="How was this resolved?"
              placeholder="e.g. Owner contacted, CCTV reviewed, police report filed"
              confirmLabel="Mark resolved"
              processing={resolving === incident.id}
              onsubmit={(note, close) => resolve(incident.id, note, close)}
            >
              {#snippet trigger()}<CheckCircle2 class="size-4" aria-hidden="true" /> Resolve{/snippet}
            </NotePopover>
          {/if}
        </div>
      {/each}
    </div>
  {:else}
    <EmptyState icon={ShieldAlert} title="Nothing to report" description="Guard consoles file incidents here — unauthorized entries, damage, patrol notes." />
  {/if}
  {#if page > 1 || hasMore}<Pagination href="/admin/incidents" {page} {hasMore} params={{ lot: filterLot ?? '', status: filterStatus ?? '', q }} />{/if}
</div>
