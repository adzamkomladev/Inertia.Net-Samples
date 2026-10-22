<script lang="ts">
  import SidePanel from '@/components/SidePanel.svelte'
  import SearchField from '@/components/SearchField.svelte'
  import type { LotCard as LotCardType } from '@/types'
  import Select from '@/components/ui/select/Select.svelte'
  import { useForm } from '@inertiajs/svelte'
  import Building2 from '@lucide/svelte/icons/building-complex'
  import Loader2 from '@lucide/svelte/icons/loader-circle'
  import Plus from '@lucide/svelte/icons/plus'
  import ShieldCheck from '@lucide/svelte/icons/shield-check'
  import LotCard from '@/components/LotCard.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import Switch from '@/components/ui/switch/Switch.svelte'

  /** GET/POST /admin/lots — directory + create form (spawns the standard A–D grid). */
  let { lots }: { lots: LotCardType[] } = $props()

  let open = $state(false)
  let q = $state('')
  let status = $state('all')
  const filtered = $derived(lots.filter(lot => `${lot.name} ${lot.area} ${lot.address}`.toLowerCase().includes(q.trim().toLowerCase()) && (status === 'all' || lot.isActive === (status === 'open'))))

  const times = Array.from({ length: 48 }, (_, i) => `${String(Math.floor(i / 2)).padStart(2, '0')}:${i % 2 ? '30' : '00'}`)
  const zones = Intl.supportedValuesOf('timeZone')
  const form = useForm({
    name: '',
    address: '',
    area: '',
    levelCount: 2,
    ratePerHourCents: 400,
    dailyMaxCents: 2400,
    contactPhone: '',
    isActive: true,
    open24Hours: true, opens: '06:00', closes: '22:00', timeZoneId: 'UTC',
    coverageCents: 50_000,
  })

  const create = () => form.post('/admin/lots')
</script>

<PageTitle title="Lots & spots" />

<div class="flex flex-col gap-6">
  <PageHeader title="Lots & spots" description="Manage availability, pricing and the places people park.">
    {#snippet actions()}<Button onclick={() => open = true}><Plus class="size-4" />Create lot</Button>{/snippet}
  </PageHeader>
  <div class="bg-card flex flex-col gap-3 rounded-xl border p-4 sm:flex-row">
    <SearchField bind:value={q} label="Search lots" placeholder="Search lots, areas or addresses…" />
    <Select bind:value={status} items={[{ value: 'all', label: 'All lots' }, { value: 'open', label: 'Open for bookings' }, { value: 'closed', label: 'Closed' }]} ariaLabel="Filter lots" class="sm:w-48" />
  </div>
  <p class="text-muted-foreground text-sm" role="status">{filtered.length} of {lots.length} lots</p>

  <div class="grid gap-5 sm:grid-cols-2 xl:grid-cols-3">
    {#each filtered as lot (lot.id)}
      <div class="relative">
        <LotCard {lot} href={`/admin/lots/${lot.id}`} />
        {#if !lot.insuranceCovered}
          <span class="bg-card absolute top-3 right-3 rounded-full border p-1.5" title="Spot Cover disabled">
            <ShieldCheck class="text-muted-foreground size-4" aria-hidden="true" />
          </span>
        {/if}
      </div>
    {:else}
      <div class="rounded-2xl border border-dashed p-12 text-center sm:col-span-2 xl:col-span-3">
        <Building2 class="text-muted-foreground mx-auto size-8" strokeWidth={1.5} aria-hidden="true" />
        <p class="font-display mt-3 font-semibold">No matching lots</p>
        <p class="text-muted-foreground mx-auto mt-1 max-w-sm text-sm">Try another search or create a new lot.</p>
      </div>
    {/each}
  </div>

  <!-- Create lot -->
  <SidePanel bind:open title="Create lot" description="Set up a location, its pricing and opening hours. A fresh bay grid is created automatically.">
    <form novalidate class="mt-4 grid gap-4 sm:grid-cols-2" onsubmit={(event: SubmitEvent) => { event.preventDefault(); create() }}>


      <div class="grid gap-2">
        <Label for="name">Name</Label>
        <Input id="name" bind:value={form.name} required maxlength="80" placeholder="Cedar Street Deck" />
        {#if form.errors.name}<p class="text-destructive text-sm" role="alert">{form.errors.name}</p>{/if}
      </div>
      <div class="grid gap-2">
        <Label for="area">Area</Label>
        <Input id="area" bind:value={form.area} required maxlength="60" placeholder="City Center" />
        {#if form.errors.area}<p class="text-destructive text-sm" role="alert">{form.errors.area}</p>{/if}
      </div>
      <div class="grid gap-2 sm:col-span-2">
        <Label for="address">Address</Label>
        <Input id="address" bind:value={form.address} required maxlength="160" placeholder="12 Cedar St, Springfield" />
        {#if form.errors.address}<p class="text-destructive text-sm" role="alert">{form.errors.address}</p>{/if}
      </div>
      <div class="grid gap-4 sm:grid-cols-3 sm:col-span-2">
        <div class="grid gap-2">
          <Label for="ratePerHourCents">Rate/hr · cents</Label>
          <Input id="ratePerHourCents" type="number" min="50" max="200000" bind:value={form.ratePerHourCents} required />
          {#if form.errors.ratePerHourCents}<p class="text-destructive text-sm" role="alert">{form.errors.ratePerHourCents}</p>{/if}
        </div>
        <div class="grid gap-2">
          <Label for="dailyMaxCents">Daily max · cents</Label>
          <Input id="dailyMaxCents" type="number" min="0" max="500000" bind:value={form.dailyMaxCents} required />
          {#if form.errors.dailyMaxCents}<p class="text-destructive text-sm" role="alert">{form.errors.dailyMaxCents}</p>{/if}
        </div>
        <div class="grid gap-2">
          <Label for="levelCount">Levels (2–4)</Label>
          <Input id="levelCount" type="number" min="2" max="4" bind:value={form.levelCount} required />
          {#if form.errors.levelCount}<p class="text-destructive text-sm" role="alert">{form.errors.levelCount}</p>{/if}
        </div>
      </div>
      <div class="grid gap-4 sm:grid-cols-2 sm:col-span-2">
        <div class="grid gap-2">
          <Label for="contactPhone">Contact phone</Label>
          <Input id="contactPhone" type="tel" bind:value={form.contactPhone} maxlength="24" placeholder="+1 555 010 4422" />
          {#if form.errors.contactPhone}<p class="text-destructive text-sm" role="alert">{form.errors.contactPhone}</p>{/if}
        </div>
        <div class="grid gap-2">
          <Label for="coverageCents">Spot Cover limit · cents</Label>
          <Input id="coverageCents" type="number" min="0" max="5000000" bind:value={form.coverageCents} required />
          {#if form.errors.coverageCents}<p class="text-destructive text-sm" role="alert">{form.errors.coverageCents}</p>{/if}
        </div>
      </div>
          <div class="space-y-3 rounded-xl border p-4 sm:col-span-2">
<h3 class="font-semibold">Opening hours</h3>
<div class="flex items-center gap-2">
<Switch id="lot-24h" bind:checked={form.open24Hours} />
<Label for="lot-24h">Open 24 hours</Label>
</div>
{#if !form.open24Hours}<div class="grid gap-3 sm:grid-cols-2">
{#each ['opens', 'closes'] as field}<div class="space-y-2">
<Label for={`lot-${field}`}>{field === 'opens' ? 'Opens' : 'Closes'}</Label>
<Select id={`lot-${field}`} value={field === 'opens' ? form.opens : form.closes} onValueChange={(value) => { if (field === 'opens') form.opens = value; else form.closes = value }} items={times.map(time => ({ value: time, label: time }))} />
</div>{/each}</div>{/if}<div class="space-y-2">
<Label for="lot-timezone">Time zone</Label>
<Select id="lot-timezone" bind:value={form.timeZoneId} items={['UTC', ...zones].map(zone => ({ value: zone, label: zone }))} />
</div>
<p class="text-xs text-muted-foreground">Daily hours apply to new bookings. Existing bookings are retained.</p>
{#if form.errors.opens}<p class="text-sm text-destructive" role="alert">{form.errors.opens}</p>{/if}</div>
      <label class="flex cursor-pointer items-center gap-3 sm:col-span-2">
        <Switch id="isActive" bind:checked={form.isActive} />
        <span class="text-sm font-medium">Open for bookings</span>
        <StatusBadge status={form.isActive ? 'free' : 'blocked'} label={form.isActive ? 'Open' : 'Closed'} size="xs" />
      </label>
      <div class="sm:col-span-2">
        <Button type="submit" size="lg"  disabled={form.processing}>
          {#if form.processing}<Loader2 class="animate-spin" aria-hidden="true" />{/if}
          {form.processing ? 'Creating…' : 'Create lot'}
        </Button>
      </div>
    </form>
  </SidePanel>
</div>
