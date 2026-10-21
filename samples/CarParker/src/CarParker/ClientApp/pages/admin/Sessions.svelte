<script lang="ts">
  import { useForm } from '@inertiajs/svelte'
  import { Car, DoorOpen, LogOut } from '@lucide/svelte'
  import DataTable from '@/components/DataTable.svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import { cn } from '@/lib/utils'
  import { fmtDate } from '@/lib/time'
  import type { SessionRow } from '@/types'

  /** GET /admin/sessions — open gates and the recent exit log. */
  let {
    openSessions,
    exitedSessions,
  }: {
    openSessions: (SessionRow & { status: string; lotName?: string | null })[]
    exitedSessions: (SessionRow & { status: string; lotName?: string | null })[]
  } = $props()

  const closeForm = useForm({ note: '' })
  let closingId = $state<string | null>(null)

  const forceClose = (id: string) => closeForm.post(`/admin/sessions/${id}/force-close`, {
    onFinish: () => { closingId = null },
  })

  const sessionCols = [
    { key: 'plate', header: 'Plate', primary: true, cell: (s: SessionRow) => s.plateNo.toUpperCase() },
    { key: 'lot', header: 'Lot', cell: (s: SessionRow & { lotName?: string | null }) => s.lotName ?? '—' },
    { key: 'spot', header: 'Spot', cell: (s: SessionRow) => s.spotLabel ?? '—' },
    { key: 'in', header: 'Entered', cell: (s: SessionRow) => fmtDate(s.enteredAtUtc, true) },
    { key: 'out', header: 'Exited', cell: (s: SessionRow) => (s.exitedAtUtc ? fmtDate(s.exitedAtUtc, true) : '—') },
    { key: 'status', header: 'Status', cell: (s: SessionRow & { status: string }) => s.status.replace('_', ' ') },
  ]
</script>

<PageTitle title="Sessions" />

<div class="flex flex-col gap-8">
  <PageHeader title="Sessions" description="Every gate event: cars on the deck now, and the recent exit log." />

  <section class="flex flex-col gap-3">
    <h2 class="font-display flex items-center gap-2 text-base font-semibold">
      <Car class="size-4" aria-hidden="true" /> On the deck now
      <span class="bg-primary/10 text-primary ml-1 rounded-full px-2 py-0.5 text-xs font-semibold">{openSessions.length}</span>
    </h2>
    {#if openSessions.length}
      <div class="flex flex-col gap-3">
        {#each openSessions as session (session.id)}
          {@const closing = closingId === session.id}
          <div class={cn('bg-card shadow-card flex flex-col gap-3 rounded-2xl border p-4 sm:flex-row sm:items-center', closing && 'opacity-60')}>
            <div class="flex min-w-0 flex-1 items-center gap-4">
              <span class="bg-primary/10 text-primary rounded-xl px-2.5 py-1 font-mono text-sm font-semibold tracking-wider">
                {session.plateNo.toUpperCase()}
              </span>
              <div class="min-w-0">
                <p class="truncate text-sm font-medium">{session.lotName ?? 'Unknown lot'} · spot {session.spotLabel ?? '—'}</p>
                <p class="text-muted-foreground text-xs">In since {fmtDate(session.enteredAtUtc, true)}</p>
              </div>
              <StatusBadge status={session.status} size="xs" class="ml-auto sm:ml-2" />
            </div>
            <form
              class="flex items-center gap-2"
              onsubmit={(e: SubmitEvent) => { e.preventDefault(); closingId = session.id; forceClose(session.id) }}
            >
              <input
                class="border-input bg-background w-40 rounded-full border px-3 py-1.5 text-sm"
                placeholder="Note (optional)"
                maxlength={160}
                bind:value={closeForm.note}
              />
              <Button type="submit" variant="outline" size="sm" disabled={closeForm.processing}>
                <LogOut class="size-3.5" aria-hidden="true" /> Force close
              </Button>
            </form>
          </div>
        {/each}
      </div>
    {:else}
      <EmptyState icon={DoorOpen} title="The decks are clear" description="No cars on the platform right now — check-ins will appear here live." />
    {/if}
  </section>

  <section class="flex flex-col gap-3">
    <h2 class="font-display text-base font-semibold">Exit log</h2>
    {#if exitedSessions.length}
      <div class="bg-card shadow-card overflow-hidden rounded-2xl border">
        <DataTable columns={sessionCols} rows={exitedSessions} rowKey={(s) => s.id} emptyTitle="No exits" />
      </div>
    {:else}
      <EmptyState icon={LogOut} title="No exits yet" description="Completed sessions will land here as guards check cars out." />
    {/if}
  </section>
</div>
