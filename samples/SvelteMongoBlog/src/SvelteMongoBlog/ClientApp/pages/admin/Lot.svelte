<script lang="ts">
  import { useForm } from '@inertiajs/svelte'
  import { CalendarRange, CarFront, CheckCircle2, Loader2, MapPin, Phone, Save, ShieldX } from '@lucide/svelte'
  import DataTable from '@/components/DataTable.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import PriceTag from '@/components/PriceTag.svelte'
  import SpotGrid from '@/components/SpotGrid.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import Switch from '@/components/ui/switch/Switch.svelte'
  import { fmtDateTime } from '@/lib/time'
  import type { BookingView, LotCard, SpotView } from '@/types'

  /**
   * GET/PUT /admin/lots/{id} + POST override — edit the lot, block/clear bays, review
   * upcoming bookings. Spot actions post block/clear with an audit note.
   */
  let {
    lot,
    spots,
    upcoming,
  }: {
    lot: LotCard
    spots: SpotView[]
    upcoming: BookingView[]
  } = $props()

  const form = useForm({
    name: lot.name,
    address: lot.address,
    area: lot.area,
    levelCount: lot.levelCount,
    ratePerHourCents: lot.ratePerHourCents,
    dailyMaxCents: lot.dailyMaxCents,
    contactPhone: lot.contactPhone ?? '',
    isActive: true,
    coverageCents: lot.coverageCents,
  })

  const save = () => form.put(`/admin/lots/${lot.id}`)

  let selected = $state<SpotView | null>(null)
  const pick = (spot: SpotView) => (selected = spot)
  const selectedState = $derived(selected ? (spots.find(s => s.id === selected!.id) ?? selected) : null)

  const overrideForm = useForm({ action: '', note: '' })
  const override = (action: 'block' | 'clear') => {
    if (!selectedState) return
    overrideForm.action = action
    overrideForm.post(`/admin/lots/${lot.id}/spots/${selectedState.id}/override`, {
      onSuccess: () => (overrideForm.note = ''),
    })
  }
</script>

<PageTitle title={`Manage · ${lot.name}`} />

{#snippet totalCell(b: BookingView)}<PriceTag cents={b.totalCents} />{/snippet}
{#snippet statusCell(b: BookingView)}<StatusBadge status={b.status} size="xs" />{/snippet}

<div class="flex flex-col gap-6">
  <PageHeader title={lot.name} description={`${lot.area} · ${lot.spots} bays across ${lot.levelCount} level${lot.levelCount === 1 ? '' : 's'}`}>
    {#snippet actions()}
      <Button href="/admin/lots" variant="outline" size="sm" class="rounded-2xl">All lots</Button>
    {/snippet}
  </PageHeader>

  <div class="grid gap-6 xl:grid-cols-[1fr_24rem]">
    <div class="flex flex-col gap-6">
      <!-- Spot board -->
      <section class="bg-card shadow-card rounded-3xl border p-5 sm:p-6">
        <div class="flex flex-wrap items-center justify-between gap-3">
          <h2 class="font-display flex items-center gap-2 text-lg font-semibold">
            <CarFront class="size-5" aria-hidden="true" /> Spot board
          </h2>
          {#if selectedState}
            <div class="flex items-center gap-2">
              <span class="bg-spot-free/15 text-spot-free-foreground rounded-full px-2.5 py-1 text-xs font-medium">{selectedState.label}</span>
              <Button size="sm" variant="outline" class="rounded-xl" disabled={overrideForm.processing || selectedState.state === 'occupied'} onclick={() => override('block')}>
                <ShieldX class="size-3.5" aria-hidden="true" /> Block
              </Button>
              <Button size="sm" variant="outline" class="rounded-xl" disabled={overrideForm.processing || selectedState.state !== 'blocked'} onclick={() => override('clear')}>
                <CheckCircle2 class="size-3.5" aria-hidden="true" /> Clear
              </Button>
            </div>
          {:else}
            <span class="text-muted-foreground text-xs">Select a bay to block or clear it</span>
          {/if}
        </div>
        <SpotGrid spots={spots} selectable selectedId={selectedState?.id} onselect={pick} compact class="mt-5" />
        {#if selectedState?.note}
          <p class="text-muted-foreground mt-4 border-t pt-3 text-xs">
            Last checkpoint: {selectedState.note}
            {#if selectedState.lastCheckpointAt} · {fmtDateTime(selectedState.lastCheckpointAt)}{/if}
          </p>
        {/if}
      </section>

      <!-- Upcoming bookings -->
      <section class="bg-card shadow-card overflow-hidden rounded-2xl border">
        <h2 class="font-display flex items-center gap-2 px-5 pt-5 pb-3 text-base font-semibold">
          <CalendarRange class="size-4" aria-hidden="true" /> Upcoming bookings
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
          <p class="text-muted-foreground border-t px-5 py-8 text-center text-sm">No confirmed bookings on this deck yet.</p>
        {/if}
      </section>
    </div>

    <!-- Edit rail -->
    <aside class="h-fit xl:sticky xl:top-24">
      <section class="bg-card shadow-card rounded-3xl border p-5">
        <h2 class="font-display flex items-center gap-2 text-base font-semibold">
          <MapPin class="text-muted-foreground size-4" aria-hidden="true" /> Lot details
        </h2>
        <form novalidate class="mt-4 grid gap-4" onsubmit={(event: SubmitEvent) => { event.preventDefault(); save() }}>
          <div class="grid gap-2">
            <Label for="name">Name</Label>
            <Input id="name" bind:value={form.name} required maxlength="80" />
            {#if form.errors.name}<p class="text-destructive text-sm" role="alert">{form.errors.name}</p>{/if}
          </div>
          <div class="grid gap-2">
            <Label for="address">Address</Label>
            <Input id="address" bind:value={form.address} required maxlength="160" />
            {#if form.errors.address}<p class="text-destructive text-sm" role="alert">{form.errors.address}</p>{/if}
          </div>
          <div class="grid gap-2">
            <Label for="area">Area</Label>
            <Input id="area" bind:value={form.area} required maxlength="60" />
            {#if form.errors.area}<p class="text-destructive text-sm" role="alert">{form.errors.area}</p>{/if}
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div class="grid gap-2">
              <Label for="ratePerHourCents">Rate/hr</Label>
              <Input id="ratePerHourCents" type="number" min="50" max="200000" bind:value={form.ratePerHourCents} required />
            </div>
            <div class="grid gap-2">
              <Label for="dailyMaxCents">Daily max</Label>
              <Input id="dailyMaxCents" type="number" min="0" max="500000" bind:value={form.dailyMaxCents} required />
            </div>
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div class="grid gap-2">
              <Label for="levelCount">Levels</Label>
              <Input id="levelCount" type="number" min="2" max="4" bind:value={form.levelCount} required />
            </div>
            <div class="grid gap-2">
              <Label for="coverageCents">Cover limit</Label>
              <Input id="coverageCents" type="number" min="0" max="5000000" bind:value={form.coverageCents} required />
            </div>
          </div>
          <div class="grid gap-2">
            <Label for="contactPhone">Contact phone</Label>
            <Input id="contactPhone" type="tel" bind:value={form.contactPhone} maxlength="24" />
          </div>
          <label class="flex cursor-pointer items-center gap-3">
            <Switch id="isActive" bind:checked={form.isActive} />
            <span class="text-sm font-medium">Open for bookings</span>
          </label>
          <Button type="submit" size="lg" class="rounded-2xl" disabled={form.processing}>
            {#if form.processing}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<Save class="size-4" aria-hidden="true" />{/if}
            {form.processing ? 'Saving…' : 'Save lot'}
          </Button>
        </form>
        <p class="text-muted-foreground mt-4 flex items-center gap-1.5 border-t pt-3 text-xs">
          <Phone class="size-3.5" aria-hidden="true" /> {lot.contactPhone ?? 'No contact phone'}
        </p>
      </section>
    </aside>
  </div>
</div>
