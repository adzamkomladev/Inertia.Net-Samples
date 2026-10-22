<script lang="ts">
  import type { LotCard as LotCardType } from '@/types'
  import { useForm } from '@inertiajs/svelte'
  import { Building2, Loader2, Plus, ShieldCheck } from '@lucide/svelte'
  import LotCard from '@/components/LotCard.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import Switch from '@/components/ui/switch/Switch.svelte'
  import { money } from '@/lib/money'

  /** GET/POST /admin/lots — directory + create form (spawns the standard A–D grid). */
  let { lots }: { lots: LotCardType[] } = $props()

  const form = useForm({
    name: '',
    address: '',
    area: '',
    levelCount: 2,
    ratePerHourCents: 400,
    dailyMaxCents: 2400,
    contactPhone: '',
    isActive: true,
    coverageCents: 50_000,
  })

  const create = () => form.post('/admin/lots')
</script>

<PageTitle title="Lots & spots" />

<div class="flex flex-col gap-6">
  <PageHeader title="Lots & spots" description="Every deck on the platform, open or parked." />

  <div class="grid gap-5 sm:grid-cols-2 xl:grid-cols-3">
    {#each lots as lot (lot.id)}
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
        <p class="font-display mt-3 font-semibold">No lots yet</p>
        <p class="text-muted-foreground mx-auto mt-1 max-w-sm text-sm">Create the first deck below — a fresh A–D spot grid is seeded automatically.</p>
      </div>
    {/each}
  </div>

  <!-- Create lot -->
  <section class="bg-card shadow-card rounded-xl p-5 sm:p-6">
    <h2 class="font-display flex items-center gap-2 text-base font-semibold">
      <Plus class="size-4" aria-hidden="true" /> New lot
    </h2>
    <p class="text-muted-foreground mt-1 text-sm">
      Creating a lot seeds its spot grid: one section per level ({money(form.ratePerHourCents)}/hr default), 12 bays per section.
    </p>
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
  </section>
</div>
