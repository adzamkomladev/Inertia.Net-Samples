<script lang="ts">
  import type { InertiaForm } from '@inertiajs/svelte'
  import Clock from '@lucide/svelte/icons/clock'
  import MapPin from '@lucide/svelte/icons/map-pin'
  import Receipt from '@lucide/svelte/icons/receipt'
  import MoneyInput from '@/components/MoneyInput.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import Select from '@/components/ui/select/Select.svelte'
  import Switch from '@/components/ui/switch/Switch.svelte'
  import Textarea from '@/components/ui/textarea/Textarea.svelte'
  import type { LotFields } from '@/lib/lot-form'

  /**
   * Lot fields shared by the create and edit sheets: location, pricing and hours.
   * `creating` shows the level count, since the bay grid is generated once at creation.
   */
  let { form, creating = false, onsubmit, id = 'lot-form' }: { form: InertiaForm<LotFields>; creating?: boolean; onsubmit: () => void; id?: string } = $props()

  const times = Array.from({ length: 48 }, (_, i) => `${String(Math.floor(i / 2)).padStart(2, '0')}:${i % 2 ? '30' : '00'}`).map(time => ({ value: time, label: time }))
  const zones = ['UTC', ...Intl.supportedValuesOf('timeZone')].map(zone => ({ value: zone, label: zone }))
  const levels = [2, 3, 4].map(n => ({ value: String(n), label: `${n} levels` }))
</script>

{#snippet error(message: string | undefined)}
  {#if message}<p class="text-destructive animate-rise text-sm" role="alert">{message}</p>{/if}
{/snippet}

<form {id} novalidate class="grid gap-8" onsubmit={(event: SubmitEvent) => { event.preventDefault(); onsubmit() }}>
  <fieldset class="grid items-start gap-4 sm:grid-cols-2">
    <legend class="mb-3 flex items-center gap-2 text-sm font-semibold"><MapPin class="text-primary size-4" aria-hidden="true" /> Location</legend>
    <div class="grid gap-2">
      <Label for={`${id}-name`}>Name</Label>
      <Input id={`${id}-name`} bind:value={form.name} required maxlength="80" placeholder="Cedar Street Deck" aria-invalid={!!form.errors.name || undefined} />
      {@render error(form.errors.name)}
    </div>
    <div class="grid gap-2">
      <Label for={`${id}-area`}>Area</Label>
      <Input id={`${id}-area`} bind:value={form.area} required maxlength="60" placeholder="City Center" aria-invalid={!!form.errors.area || undefined} />
      {@render error(form.errors.area)}
    </div>
    <div class="grid gap-2 sm:col-span-2">
      <Label for={`${id}-address`}>Address</Label>
      <Input id={`${id}-address`} bind:value={form.address} required maxlength="160" placeholder="12 Cedar St, Springfield" aria-invalid={!!form.errors.address || undefined} />
      {@render error(form.errors.address)}
    </div>
    <div class="grid gap-2">
      <Label for={`${id}-phone`}>Contact phone</Label>
      <Input id={`${id}-phone`} type="tel" bind:value={form.contactPhone} maxlength="24" placeholder="+1 555 010 4422" aria-invalid={!!form.errors.contactPhone || undefined} />
      {@render error(form.errors.contactPhone)}
    </div>
    {#if creating}
      <div class="grid gap-2">
        <Label for={`${id}-levels`}>Levels</Label>
        <Select id={`${id}-levels`} value={String(form.levelCount)} onValueChange={(value) => (form.levelCount = Number(value))} items={levels} class="w-full" />
        {@render error(form.errors.levelCount)}
      </div>
    {/if}
    <div class="grid gap-2 sm:col-span-2">
      <div class="flex items-baseline justify-between gap-2">
        <Label for={`${id}-arrival`}>Arrival notes</Label>
        <span class="text-muted-foreground text-xs tabular-nums">{form.arrivalNotes.length}/280</span>
      </div>
      <Textarea id={`${id}-arrival`} bind:value={form.arrivalNotes} maxlength="280" rows={3} autosize placeholder="Entrance, height limit, gate codes: shown to drivers before they arrive." />
      {@render error(form.errors.arrivalNotes)}
    </div>
  </fieldset>

  <fieldset class="grid items-start gap-4 sm:grid-cols-3">
    <legend class="mb-3 flex items-center gap-2 text-sm font-semibold"><Receipt class="text-primary size-4" aria-hidden="true" /> Pricing and cover</legend>
    <div class="grid gap-2">
      <Label for={`${id}-rate`}>Per hour</Label>
      <MoneyInput id={`${id}-rate`} bind:cents={form.ratePerHourCents} min={50} max={200_000} invalid={!!form.errors.ratePerHourCents} />
      {@render error(form.errors.ratePerHourCents)}
    </div>
    <div class="grid gap-2">
      <Label for={`${id}-daily`}>Daily max</Label>
      <MoneyInput id={`${id}-daily`} bind:cents={form.dailyMaxCents} max={500_000} invalid={!!form.errors.dailyMaxCents} />
      {@render error(form.errors.dailyMaxCents)}
    </div>
    <div class="grid gap-2">
      <Label for={`${id}-cover`}>Spot Cover limit</Label>
      <MoneyInput id={`${id}-cover`} bind:cents={form.coverageCents} max={5_000_000} invalid={!!form.errors.coverageCents} />
      {@render error(form.errors.coverageCents)}
    </div>
  </fieldset>

  <fieldset class="grid gap-4">
    <legend class="mb-3 flex items-center gap-2 text-sm font-semibold"><Clock class="text-primary size-4" aria-hidden="true" /> Hours and availability</legend>
    <label class="bg-muted/60 hover:bg-muted flex cursor-pointer items-center justify-between gap-3 rounded-xl p-3.5 transition-colors">
      <span>
        <span class="block text-sm font-medium">Open 24 hours</span>
        <span class="text-muted-foreground block text-xs">Turn off to set daily opening and closing times.</span>
      </span>
      <Switch id={`${id}-24h`} bind:checked={form.open24Hours} />
    </label>
    {#if !form.open24Hours}
      <div class="animate-rise grid gap-3 sm:grid-cols-2">
        <div class="grid gap-2">
          <Label for={`${id}-opens`}>Opens</Label>
          <Select id={`${id}-opens`} bind:value={form.opens} items={times} class="w-full" />
        </div>
        <div class="grid gap-2">
          <Label for={`${id}-closes`}>Closes</Label>
          <Select id={`${id}-closes`} bind:value={form.closes} items={times} class="w-full" />
        </div>
      </div>
    {/if}
    <div class="grid gap-2">
      <Label for={`${id}-tz`}>Time zone</Label>
      <Select id={`${id}-tz`} bind:value={form.timeZoneId} items={zones} class="w-full" />
      <p class="text-muted-foreground text-xs">Hours apply to new bookings; existing bookings are kept.</p>
    </div>
    {@render error(form.errors.opens)}
    <label class="bg-muted/60 hover:bg-muted flex cursor-pointer items-center justify-between gap-3 rounded-xl p-3.5 transition-colors">
      <span class="flex items-center gap-2 text-sm font-medium">
        Open for bookings
        <StatusBadge status={form.isActive ? 'free' : 'blocked'} label={form.isActive ? 'Open' : 'Closed'} size="xs" />
      </span>
      <Switch id={`${id}-active`} bind:checked={form.isActive} />
    </label>
  </fieldset>
</form>
