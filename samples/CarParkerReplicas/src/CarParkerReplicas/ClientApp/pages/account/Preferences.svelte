<script lang="ts">
  import { untrack } from 'svelte'
  import { useForm } from '@inertiajs/svelte'
  import Loader2 from '@lucide/svelte/icons/loader-circle'
  import Save from '@lucide/svelte/icons/save'
  import ImageUpload from '@/components/ImageUpload.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import Select from '@/components/ui/select/Select.svelte'
  import Switch from '@/components/ui/switch/Switch.svelte'
  import { applyTheme } from '@/lib/theme.svelte'
  import type { Theme, UserPreferences, Vehicle } from '@/types'
  import CarFront from '@lucide/svelte/icons/car-front'
  import Plus from '@lucide/svelte/icons/plus'
  import Star from '@lucide/svelte/icons/star'
  import Trash2 from '@lucide/svelte/icons/trash-2'
  import { cn } from '@/lib/utils'

  /** GET/PUT /account/profile and /account/preferences — one page component, props split by section. */
  let {
    profile,
    preferences,
    vehicles = [],
  }: {
    profile: { name: string; email: string; phone: string | null; avatarAssetId: string | null }
    preferences: UserPreferences
    vehicles?: Vehicle[]
  } = $props()

  // Saved vehicles: every change PUTs the whole list (at most five), so there is no per-row endpoint.
  const vehiclesForm = useForm({ vehicles: [] as Vehicle[] })
  const newVehicle = useForm({ plate: '', label: '' })
  let adding = $state(false)
  let busyPlate = $state<string | null>(null)
  const saveVehicles = (next: Vehicle[], busy: string, onSuccess?: () => void) => {
    busyPlate = busy
    vehiclesForm.transform(() => ({ vehicles: next })).put('/account/vehicles', {
      preserveScroll: true,
      onSuccess: () => onSuccess?.(),
      onFinish: () => (busyPlate = null),
    })
  }
  const addVehicle = () => {
    const plate = newVehicle.plate.trim().toUpperCase()
    if (!plate) return
    saveVehicles([...vehicles, { plate, label: newVehicle.label.trim() || null, isDefault: !vehicles.length }], plate, () => {
      newVehicle.reset()
      adding = false
    })
  }
  const makeDefault = (plate: string) => saveVehicles(vehicles.map(v => ({ ...v, isDefault: v.plate === plate })), plate)
  const removeVehicle = (plate: string) => saveVehicles(vehicles.filter(v => v.plate !== plate), plate)

  const themeItems = [
    { value: 'system', label: 'System' },
    { value: 'light', label: 'Light' },
    { value: 'dark', label: 'Dark' },
  ]

  const profileForm = useForm(untrack(() => ({
    name: profile.name,
    phone: profile.phone ?? '',
    avatarAssetId: profile.avatarAssetId,
  })))

  const prefsForm = useForm('put', '/account/preferences', untrack(() => ({ ...preferences })))

  const saveProfile = () => profileForm.put('/account/profile')
  const savePrefs = () =>
    prefsForm.put('/account/preferences', {
      onSuccess: () => applyTheme(prefsForm.theme as Theme),
    })
</script>

<PageTitle title="Profile & settings" />
<div class="flex max-w-3xl flex-col gap-6">
  <PageHeader title="Profile & settings" description="Your details and how CarParker talks to you." />

  <!-- Profile -->
  <section class="bg-card shadow-card rounded-xl p-5 sm:p-6">
    <h2 class="font-display text-base font-semibold">Profile</h2>
    <form novalidate class="mt-4 grid gap-4" onsubmit={(event: SubmitEvent) => { event.preventDefault(); saveProfile() }}>
      <div class="flex flex-col items-start gap-4 sm:flex-row sm:items-center">
        <ImageUpload
          bind:value={profileForm.avatarAssetId}
          label="Avatar"
          previewClass="size-16 rounded-full object-cover"
        />
        <p class="text-muted-foreground text-sm leading-5">
          Shown next to your bookings. PNG or JPEG up to 5&nbsp;MB.
        </p>
      </div>
      <div class="grid gap-4 sm:grid-cols-2">
        <div class="grid gap-2">
          <Label for="name">Name</Label>
          <Input id="name" bind:value={profileForm.name} required minlength={2} maxlength={80} />
          {#if profileForm.errors.name}<p class="text-destructive text-sm" role="alert">{profileForm.errors.name}</p>{/if}
        </div>
        <div class="grid gap-2">
          <Label for="phone">Phone</Label>
          <Input id="phone" type="tel" bind:value={profileForm.phone} maxlength={24} placeholder="+1 555 010 4422" />
          {#if profileForm.errors.phone}<p class="text-destructive text-sm" role="alert">{profileForm.errors.phone}</p>{/if}
        </div>
        <div class="grid gap-2">
          <Label for="email">E-mail</Label>
          <Input id="email" type="email" value={profile.email} disabled />
          <p class="text-muted-foreground text-xs">Your sign-in address — contact support to change it.</p>
        </div>
      </div>
      <Button type="submit" class="w-fit" disabled={profileForm.processing}>
        {#if profileForm.processing}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<Save class="size-4" aria-hidden="true" />{/if}
        {profileForm.processing ? 'Saving…' : 'Save profile'}
      </Button>
    </form>
  </section>

  <!-- Vehicles -->
  <section class="bg-card shadow-card rounded-xl p-5 sm:p-6" aria-labelledby="vehicles-title">
    <div class="flex flex-wrap items-start justify-between gap-3">
      <div>
        <h2 id="vehicles-title" class="font-display text-base font-semibold">Your vehicles</h2>
        <p class="text-muted-foreground mt-1 text-sm">Saved plates are one tap away at checkout; the default is filled in for you.</p>
      </div>
      {#if !adding && vehicles.length < 5}
        <Button variant="outline" size="sm" onclick={() => (adding = true)}><Plus class="size-4" aria-hidden="true" /> Add vehicle</Button>
      {/if}
    </div>

    {#if vehicles.length}
      <ul class="mt-4 grid gap-2">
        {#each vehicles as vehicle (vehicle.plate)}
          <li class={cn('animate-rise flex min-w-0 items-center gap-2 rounded-xl p-3 sm:gap-3 ring-1 ring-inset transition-[background-color,opacity]', vehicle.isDefault ? 'bg-primary/5 ring-primary/30' : 'ring-border', busyPlate === vehicle.plate && 'opacity-60')}>
            <span class="plate text-base">{vehicle.plate}</span>
            <span class="min-w-0 flex-1 truncate text-sm">{vehicle.label ?? ''}</span>
            {#if vehicle.isDefault}
              <span class="text-primary inline-flex shrink-0 items-center gap-1 text-xs font-semibold"><Star class="size-3.5 fill-current" aria-hidden="true" /> Default</span>
            {:else}
              <Button variant="ghost" size="xs" disabled={!!busyPlate} aria-label={`Make ${vehicle.plate} the default`} onclick={() => makeDefault(vehicle.plate)}><Star aria-hidden="true" /><span class="hidden sm:inline">Make default</span></Button>
            {/if}
            <Button variant="ghost" size="icon-xs" class="hover:text-destructive hover:bg-destructive/10" disabled={!!busyPlate} aria-label={`Remove ${vehicle.plate}`} onclick={() => removeVehicle(vehicle.plate)}>
              {#if busyPlate === vehicle.plate}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<Trash2 aria-hidden="true" />{/if}
            </Button>
          </li>
        {/each}
      </ul>
    {:else if !adding}
      <div class="text-muted-foreground mt-4 flex items-center gap-3 rounded-xl border border-dashed p-4 text-sm">
        <CarFront class="size-5 shrink-0" aria-hidden="true" /> No vehicles yet. Add your plate to speed through checkout.
      </div>
    {/if}

    {#if adding}
      <form novalidate class="bg-muted/60 animate-rise mt-4 grid gap-3 rounded-xl p-4 sm:grid-cols-[10rem_1fr_auto] sm:items-end" onsubmit={(event: SubmitEvent) => { event.preventDefault(); addVehicle() }}>
        <div class="grid gap-2">
          <Label for="vehicle-plate">Plate</Label>
          <!-- svelte-ignore a11y_autofocus -->
          <Input id="vehicle-plate" bind:value={newVehicle.plate} maxlength="16" required autofocus autocomplete="off" placeholder="ABX 442" class="font-display font-bold tracking-wider uppercase" />
        </div>
        <div class="grid gap-2">
          <Label for="vehicle-label">Nickname <span class="text-muted-foreground font-normal">(optional)</span></Label>
          <Input id="vehicle-label" bind:value={newVehicle.label} maxlength="40" placeholder="Family car" />
        </div>
        <div class="flex gap-2">
          <Button variant="ghost" onclick={() => { adding = false; newVehicle.reset(); vehiclesForm.clearErrors() }}>Cancel</Button>
          <Button type="submit" disabled={!newVehicle.plate.trim() || !!busyPlate}>
            {#if busyPlate}<Loader2 class="animate-spin" aria-hidden="true" />{/if} Save
          </Button>
        </div>
      </form>
    {/if}
    {#if vehiclesForm.errors.vehicles}<p class="text-destructive animate-rise mt-3 text-sm" role="alert">{vehiclesForm.errors.vehicles}</p>{/if}
  </section>

  <!-- Preferences -->
  <section class="bg-card shadow-card rounded-xl p-5 sm:p-6">
    <h2 class="font-display text-base font-semibold">Preferences</h2>
    <form novalidate class="mt-4 grid gap-5" onsubmit={(event: SubmitEvent) => { event.preventDefault(); savePrefs() }}>
      <div class="grid gap-2">
        <Label for="theme">Theme</Label>
        <Select id="theme" items={themeItems} bind:value={prefsForm.theme} ariaLabel="Theme" class="w-full sm:w-48" />
        {#if prefsForm.errors.theme}<p class="text-destructive text-sm" role="alert">{prefsForm.errors.theme}</p>{/if}
      </div>
      <div class="grid gap-3">
        {#each [['bookingReminders', 'Booking reminders', 'A nudge before each window starts.'],
                ['emailOnCheckout', 'E-mail on check-out', 'A receipt when a session ends on your spot.'],
                ['emailOnRefund', 'Refund e-mails', 'Decisions and payouts on your refund requests.']] as [key, label, description]}
          <label class="flex cursor-pointer items-start gap-3">
            <Switch id={key} bind:checked={prefsForm[key as 'bookingReminders' | 'emailOnCheckout' | 'emailOnRefund']} class="mt-0.5" />
            <span>
              <span class="text-sm font-medium">{label}</span>
              <span class="text-muted-foreground block text-sm">{description}</span>
            </span>
          </label>
        {/each}
      </div>
      <Button type="submit" class="w-fit" disabled={prefsForm.processing}>
        {#if prefsForm.processing}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<Save class="size-4" aria-hidden="true" />{/if}
        {prefsForm.processing ? 'Saving…' : 'Save preferences'}
      </Button>
    </form>
  </section>
</div>
