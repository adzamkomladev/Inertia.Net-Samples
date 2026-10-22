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
  import type { Theme, UserPreferences } from '@/types'

  /** GET/PUT /account/profile and /account/preferences — one page component, props split by section. */
  let {
    profile,
    preferences,
  }: {
    profile: { name: string; email: string; phone: string | null; avatarAssetId: string | null }
    preferences: UserPreferences
  } = $props()

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
