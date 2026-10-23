<script lang="ts">
  import { untrack } from 'svelte'
  import { useForm } from '@inertiajs/svelte'
  import DataTable from '@/components/DataTable.svelte'
  import Pagination from '@/components/Pagination.svelte'
  import SearchField from '@/components/SearchField.svelte'
  import SidePanel from '@/components/SidePanel.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import ArrowUpRight from '@lucide/svelte/icons/arrow-up-right'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import ConfirmDialog from '@/components/ConfirmDialog.svelte'
  import { useDebouncedSearch } from '@/lib/debounced-search.svelte'
  import { fmtDateTime } from '@/lib/time'
  import type { SessionRow } from '@/types'
  type Row = SessionRow & { status: string; lotName?: string | null; cancelReason?: string | null }
  let { openSessions, exitedSessions, query = '', openPage = 1, openHasMore = false, openCount = 0, exitedPage = 1, exitedHasMore = false, exitedCount = 0 }: {
    openSessions: Row[]; exitedSessions: Row[]; query?: string
    openPage: number; openHasMore: boolean; openCount: number
    exitedPage: number; exitedHasMore: boolean; exitedCount: number
  } = $props()
  let q = $state(untrack(() => query))
  $effect.pre(() => { q = query })
  useDebouncedSearch(() => q, '/admin/sessions')
  let tab = $state('open')
  let selectedId = $state<string | null>(null)
  let panelOpen = $state(false)
  const selected = $derived([...openSessions, ...exitedSessions].find(s => s.id === selectedId))
  const rows = $derived(tab === 'open' ? openSessions : exitedSessions)
  const form = useForm({ note: '' })
  function inspect(row: Row) { selectedId = row.id; form.reset(); form.clearErrors(); panelOpen = true }
</script>
<PageTitle title="Sessions" />
{#snippet plateCell(row: Row)}<button class="group inline-flex min-h-10 items-center gap-1.5 rounded-full border px-3 text-left text-xs font-semibold transition hover:bg-accent" onclick={() => inspect(row)} aria-label="View session details for {row.plateNo.toUpperCase()}">{row.plateNo.toUpperCase()}<ArrowUpRight class="size-3.5 transition-transform group-hover:translate-x-0.5 group-hover:-translate-y-0.5" /></button>{/snippet}
<div class="space-y-6">
  <PageHeader title="Parking sessions" description="See who is on site, review exits and resolve open sessions." />
  <div class="grid gap-4 sm:grid-cols-2">
    <div class="bg-card rounded-xl border p-5"><p class="text-muted-foreground text-sm">Currently on site</p><p class="mt-2 text-3xl font-semibold tabular-nums">{openCount}</p></div>
    <div class="bg-card rounded-xl border p-5"><p class="text-muted-foreground text-sm">Recent exits</p><p class="mt-2 text-3xl font-semibold tabular-nums">{exitedCount}</p></div>
  </div>
  <SearchField bind:value={q} label="Search sessions" placeholder="Search by plate, lot or bay…" />
  <div class="bg-card overflow-hidden rounded-xl border">
    <div class="flex gap-2 border-b p-3" role="group" aria-label="Session status">
      {#each [{ value: 'open', label: 'On site', count: openCount }, { value: 'exited', label: 'Exit log', count: exitedCount }] as item}
        <Button variant={tab === item.value ? 'default' : 'ghost'} aria-pressed={tab === item.value} onclick={() => tab = item.value}>{item.label} <span class="ml-2 opacity-70">{item.count}</span></Button>
      {/each}
    </div>
    <DataTable {rows} rowKey={s => s.id} emptyTitle="No matching sessions" emptyDescription="Try a different plate, lot, bay or session status." columns={[
      { key: 'plate', header: 'Plate', primary: true, snippet: plateCell },
      { key: 'lot', header: 'Lot', cell: (s: Row) => s.lotName ?? 'Unknown lot' },
      { key: 'spot', header: 'Bay', cell: (s: Row) => s.spotLabel ?? '—' },
      { key: 'in', header: 'Entered', cell: (s: Row) => fmtDateTime(s.enteredAtUtc) },
      { key: 'out', header: 'Exited', cell: (s: Row) => s.exitedAtUtc ? fmtDateTime(s.exitedAtUtc) : 'Still on site' },
    ]} />
    {#if tab === 'open' && (openPage > 1 || openHasMore)}<Pagination href="/admin/sessions" page={openPage} hasMore={openHasMore} pageParam="openPage" params={{ q: query, exitedPage }} />{/if}
    {#if tab === 'exited' && (exitedPage > 1 || exitedHasMore)}<Pagination href="/admin/sessions" page={exitedPage} hasMore={exitedHasMore} pageParam="exitedPage" params={{ q: query, openPage }} />{/if}
  </div>
  <p class="text-muted-foreground text-sm" role="status">Open a plate to view its details; use the page controls to browse the full session log.</p>
</div>
<SidePanel bind:open={panelOpen} title={selected?.plateNo.toUpperCase() ?? 'Session details'} description="Parking session details and operator actions. All times are UTC.">
  {#if selected}
    <div class="space-y-6">
      <StatusBadge status={selected.status} label={selected.exitedAtUtc ? 'Exited' : 'On site'} />
      <dl class="grid grid-cols-2 gap-5 text-sm">
        {#each [{ label: 'Lot', value: selected.lotName ?? 'Unknown lot' }, { label: 'Bay', value: selected.spotLabel ?? '—' }, { label: 'Entered', value: fmtDateTime(selected.enteredAtUtc) }, { label: 'Exited', value: selected.exitedAtUtc ? fmtDateTime(selected.exitedAtUtc) : 'Still on site' }] as item}
          <div><dt class="text-muted-foreground">{item.label}</dt><dd class="mt-1 font-medium">{item.value}</dd></div>
        {/each}
      </dl>
      {#if selected.cancelReason}<div class="bg-muted rounded-lg p-4 text-sm"><p class="font-medium">Closure note</p><p class="mt-2">{selected.cancelReason}</p></div>{/if}
      {#if !selected.exitedAtUtc}
        <div class="space-y-4 border-t pt-6">
          <h2 class="font-semibold">Close this session</h2>
          <p class="text-muted-foreground text-sm">Use only after confirming the vehicle has left. This releases the bay.</p>
          <Label for="session-note">Closure note (optional)</Label>
          <Input id="session-note" bind:value={form.note} maxlength={160} placeholder="Why is this session being closed?" />
          {#each Object.values(form.errors) as error}<p role="alert" class="text-destructive text-sm">{error}</p>{/each}
          <ConfirmDialog title="Force close this session?" description="Confirm that the vehicle has left before releasing its bay." confirmLabel="Force close" destructive onConfirm={() => { if (selected && !form.processing) form.post(`/admin/sessions/${selected.id}/force-close?note=${encodeURIComponent(form.note)}`, { onSuccess: () => panelOpen = false }) }} triggerClass="bg-destructive text-white min-h-11 rounded-lg px-4 text-sm font-medium disabled:opacity-50">
            {#snippet trigger()}{form.processing ? 'Closing…' : 'Force close session'}{/snippet}
          </ConfirmDialog>
        </div>
      {/if}
    </div>
  {/if}
</SidePanel>
