<script lang="ts">
  import { untrack } from 'svelte'
  import { useForm } from '@inertiajs/svelte'
  import CalendarRange from '@lucide/svelte/icons/calendar-range'
  import CarFront from '@lucide/svelte/icons/car-front'
  import CheckCircle2 from '@lucide/svelte/icons/circle-check'
  import ExternalLink from '@lucide/svelte/icons/external-link'
  import Loader2 from '@lucide/svelte/icons/loader-circle'
  import MousePointerClick from '@lucide/svelte/icons/mouse-pointer-click'
  import Pencil from '@lucide/svelte/icons/pencil'
  import Save from '@lucide/svelte/icons/save'
  import ShieldX from '@lucide/svelte/icons/shield-x'
  import X from '@lucide/svelte/icons/x'
  import DataTable from '@/components/DataTable.svelte'
  import LotForm from '@/components/LotForm.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import PriceTag from '@/components/PriceTag.svelte'
  import SidePanel from '@/components/SidePanel.svelte'
  import SpotGrid from '@/components/SpotGrid.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import Textarea from '@/components/ui/textarea/Textarea.svelte'
  import { lotFields } from '@/lib/lot-form'
  import { money } from '@/lib/money'
  import { fmtDateTime } from '@/lib/time'
  import type { BookingView, LotCard, SpotView } from '@/types'

  type AdminSpot = SpotView & { checkpointNote?: string | null }

  /**
   * GET/PUT /admin/lots/{id} + POST override: the live spot board with a bay inspector,
   * a read-only lot summary and an edit sheet. Block/clear posts carry an audit note.
   */
  let { lot, spots, upcoming }: { lot: LotCard; spots: AdminSpot[]; upcoming: BookingView[] } = $props()

  let editing = $state(false)
  const form = useForm(untrack(() => lotFields(lot)))
  const save = () => form.put(`/admin/lots/${lot.id}`, { preserveScroll: true, onSuccess: () => (editing = false) })
  const openEdit = () => {
    form.defaults(lotFields(lot))
    form.reset()
    form.clearErrors()
    editing = true
  }

  let selectedId = $state<string | null>(null)
  const selected = $derived(spots.find(s => s.id === selectedId) ?? null)
  const counts = $derived(
    (['free', 'booked', 'occupied', 'blocked'] as const).map(state => ({ state, count: spots.filter(s => s.state === state).length })),
  )

  const overrideForm = useForm({ action: '', note: '' })
  const override = (action: 'block' | 'clear') => {
    if (!selected) return
    overrideForm.action = action
    overrideForm.post(`/admin/lots/${lot.id}/spots/${selected.id}/override`, {
      preserveScroll: true,
      onSuccess: () => overrideForm.reset('note'),
    })
  }

  const hours = $derived(lot.open24Hours ? 'Open 24 hours' : `${lot.opens}–${lot.closes} daily`)
</script>

<PageTitle title={`Manage · ${lot.name}`} />

{#snippet totalCell(b: BookingView)}<PriceTag cents={b.totalCents} />{/snippet}
{#snippet statusCell(b: BookingView)}<StatusBadge status={b.status} size="xs" />{/snippet}

<div class="flex flex-col gap-6">
  <PageHeader title={lot.name}>
    {#snippet description()}
      <span class="inline-flex flex-wrap items-center gap-2">
        {lot.area} · {lot.spots} bays on {lot.levelCount} levels
        <StatusBadge status={lot.isActive ? 'free' : 'blocked'} label={lot.isActive ? 'Open for bookings' : 'Closed'} size="xs" />
      </span>
    {/snippet}
    {#snippet actions()}
      {#if lot.isActive}<Button href={`/lots/${lot.id}`} variant="outline" size="sm"><ExternalLink class="size-4" aria-hidden="true" /> Public page</Button>{/if}
      <Button size="sm" onclick={openEdit}><Pencil class="size-4" aria-hidden="true" /> Edit lot</Button>
    {/snippet}
  </PageHeader>

  <!-- Live counts: same colour code as the floor plan -->
  <dl class="grid grid-cols-2 gap-3 sm:grid-cols-4">
    {#each counts as { state, count } (state)}
      <div class="bg-card shadow-card flex items-center gap-3 rounded-xl p-4">
        <span class={['size-3 shrink-0 rounded-full', state === 'free' && 'bg-spot-free', state === 'booked' && 'bg-spot-booked', state === 'occupied' && 'bg-spot-occupied', state === 'blocked' && 'bg-spot-blocked stripes-blocked ring-spot-blocked-foreground/30 ring-1']} aria-hidden="true"></span>
        <div>
          <dt class="text-muted-foreground text-xs font-medium capitalize">{state}</dt>
          <dd class="font-display text-2xl leading-none font-bold tabular-nums">{count}</dd>
        </div>
      </div>
    {/each}
  </dl>

  <div class="grid items-start gap-6 xl:grid-cols-[1fr_22rem]">
    <!-- Spot board -->
    <section class="bg-card shadow-card rounded-xl p-5 sm:p-6" aria-labelledby="board-title">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <h2 id="board-title" class="font-display flex items-center gap-2 text-lg font-semibold">
          <CarFront class="size-5" aria-hidden="true" /> Spot board
        </h2>
        <span class="text-muted-foreground inline-flex items-center gap-1.5 text-xs"><MousePointerClick class="size-3.5" aria-hidden="true" /> Select a bay to inspect, block or clear it</span>
      </div>
      <SpotGrid spots={spots} selectable selectedId={selected?.id} selectableStates={['free', 'booked', 'occupied', 'blocked']} onselect={(spot) => (selectedId = spot.id)} class="mt-5" />
    </section>

    <div class="flex flex-col gap-6 xl:sticky xl:top-8">
      <!-- Bay inspector -->
      {#if selected}
        {#key selected.id}
          <section class="bg-card shadow-lifted ring-primary/25 animate-rise rounded-xl p-5 ring-2" aria-labelledby="bay-title" aria-live="polite">
            <div class="flex items-start justify-between gap-3">
              <div>
                <p class="text-muted-foreground text-xs font-semibold tracking-wider uppercase">Level {selected.level} · {selected.type}</p>
                <h2 id="bay-title" class="font-display mt-0.5 text-2xl font-bold">Bay {selected.label}</h2>
              </div>
              <button type="button" class="hover:bg-muted grid size-9 place-items-center rounded-lg transition-colors" aria-label="Close bay inspector" onclick={() => (selectedId = null)}><X class="size-4" /></button>
            </div>
            <StatusBadge status={selected.state} class="mt-3" />
            {#if selected.checkpointNote || selected.lastCheckpointAt}
              <p class="bg-muted text-muted-foreground mt-4 rounded-lg p-3 text-xs leading-5">
                <span class="text-foreground font-semibold">Last checkpoint</span>
                {#if selected.lastCheckpointAt} · {fmtDateTime(selected.lastCheckpointAt)}{/if}
                {#if selected.checkpointNote}<br />{selected.checkpointNote}{/if}
              </p>
            {/if}
            <div class="mt-4 grid gap-2">
              <Label for="override-note">Audit note</Label>
              <Textarea id="override-note" bind:value={overrideForm.note} maxlength="280" rows={2} placeholder="Why is this bay changing? e.g. Oil spill, cones placed" />
              {#if overrideForm.errors.note}<p class="text-destructive text-sm" role="alert">{overrideForm.errors.note}</p>{/if}
            </div>
            <div class="mt-4 grid grid-cols-2 gap-2">
              <Button variant="outline" disabled={overrideForm.processing || selected.state === 'occupied' || selected.state === 'blocked'} onclick={() => override('block')}>
                {#if overrideForm.processing && overrideForm.action === 'block'}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<ShieldX class="size-4" aria-hidden="true" />{/if} Block
              </Button>
              <Button disabled={overrideForm.processing || selected.state !== 'blocked'} onclick={() => override('clear')}>
                {#if overrideForm.processing && overrideForm.action === 'clear'}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<CheckCircle2 class="size-4" aria-hidden="true" />{/if} Clear
              </Button>
            </div>
            {#if selected.state === 'occupied'}<p class="text-muted-foreground mt-3 text-xs">A car is on this bay; block it after the session ends.</p>{/if}
          </section>
        {/key}
      {/if}

      <!-- Lot summary -->
      <section class="bg-card shadow-card rounded-xl p-5" aria-labelledby="details-title">
        <div class="flex items-center justify-between gap-2">
          <h2 id="details-title" class="font-display text-base font-semibold">Lot details</h2>
          <Button variant="ghost" size="xs" onclick={openEdit}><Pencil aria-hidden="true" /> Edit</Button>
        </div>
        <dl class="mt-3 divide-y text-sm">
          {#each [
            ['Address', lot.address],
            ['Hours', `${hours} · ${lot.timeZoneId}`],
            ['Per hour', money(lot.ratePerHourCents)],
            ['Daily max', money(lot.dailyMaxCents)],
            ['Spot Cover limit', lot.insuranceCovered ? money(lot.coverageCents) : 'Off'],
            ['Contact', lot.contactPhone ?? '—'],
          ] as [term, value] (term)}
            <div class="flex items-baseline justify-between gap-4 py-2.5">
              <dt class="text-muted-foreground">{term}</dt>
              <dd class="text-right font-medium tabular-nums">{value}</dd>
            </div>
          {/each}
        </dl>
        <div class="bg-muted mt-2 rounded-lg p-3">
          <p class="text-xs font-semibold">Arrival notes</p>
          <p class="text-muted-foreground mt-1 text-sm leading-6">{lot.arrivalNotes ?? 'None yet. Add entrance and height-limit tips for drivers.'}</p>
        </div>
      </section>
    </div>
  </div>

  <!-- Upcoming bookings -->
  <section class="bg-card shadow-card overflow-hidden rounded-xl" aria-labelledby="upcoming-title">
    <h2 id="upcoming-title" class="font-display flex items-center gap-2 px-5 pt-5 pb-3 text-base font-semibold">
      <CalendarRange class="size-4" aria-hidden="true" /> Upcoming bookings
      <span class="bg-muted text-muted-foreground rounded-full px-2 text-xs tabular-nums">{upcoming.length}</span>
    </h2>
    {#if upcoming.length}
      <div class="border-t">
        <DataTable
          rows={upcoming}
          rowKey={(b) => b.id}
          rowHref={(b) => `/admin/bookings/${b.id}`}
          columns={[
            { key: 'spot', header: 'Spot', primary: true, cell: (b: BookingView) => b.spotLabel },
            { key: 'window', header: 'Window', cell: (b: BookingView) => (b.windowType === 'recurring' ? `${b.occurrenceCount}× weekly` : fmtDateTime(b.startAtUtc)) },
            { key: 'plate', header: 'Plate', desktopOnly: true, cell: (b: BookingView) => b.plateNo ?? '—' },
            { key: 'total', header: 'Total', align: 'right' as const, snippet: totalCell },
            { key: 'status', header: 'Status', align: 'right' as const, snippet: statusCell },
          ]}
        />
      </div>
    {:else}
      <p class="text-muted-foreground border-t px-5 py-10 text-center text-sm">No confirmed bookings at this lot yet.</p>
    {/if}
  </section>
</div>

<SidePanel bind:open={editing} title="Edit lot" description="Changes apply to new bookings right away; bays and existing bookings are kept.">
  <LotForm {form} id="edit-lot" onsubmit={save} />
  {#snippet footer()}
    <Button variant="ghost" onclick={() => (editing = false)}>Cancel</Button>
    <Button type="submit" form="edit-lot" disabled={form.processing || !form.isDirty}>
      {#if form.processing}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<Save class="size-4" aria-hidden="true" />{/if}
      {form.processing ? 'Saving…' : 'Save changes'}
    </Button>
  {/snippet}
</SidePanel>
