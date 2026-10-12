<script lang="ts">
  import { useForm, usePage } from '@inertiajs/svelte'
  import { Check, Loader2 } from '@lucide/svelte'
  import ImageUpload from '@/components/ImageUpload.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import Select from '@/components/ui/select/Select.svelte'
  import Switch from '@/components/ui/switch/Switch.svelte'
  import { applyTheme } from '@/lib/theme.svelte'
  import { cn } from '@/lib/utils'
  import type { AuthUser, Preferences, Theme } from '@/types'

  let { preferences, profile }: { preferences: Preferences; profile: { name: string; avatarAssetId: string | null } } = $props()

  const page = usePage()
  const user = $derived((page.props.auth as { user: AuthUser | null } | undefined)?.user ?? null)
  let uploadingAvatar = $state(false)

  // svelte-ignore state_referenced_locally
  const profileForm = useForm({ name: profile.name, avatarAssetId: profile.avatarAssetId })
  // svelte-ignore state_referenced_locally
  const prefsForm = useForm({ ...preferences, dailyRecommendationDigest: preferences.dailyRecommendationDigest ?? false, timeZoneId: preferences.timeZoneId ?? 'UTC' })

  const themes = [
    { value: 'system', label: 'System' },
    { value: 'light', label: 'Light' },
    { value: 'dark', label: 'Dark' },
  ] as const
  const pageSizes = [5, 10, 20, 50]
  const toggles = [
    { key: 'emailOnComment', label: 'New comments', hint: 'Email me when someone comments on my post.' },
    { key: 'emailOnLike', label: 'New likes', hint: 'Email me when someone likes my post.' },
    { key: 'dailyRecommendationDigest', label: 'Daily recommendations', hint: 'Yesterday’s recommended posts you haven’t seen yet.' },
    { key: 'weeklyDigest', label: 'Weekly digest', hint: 'A weekly summary of views, likes and comments.' },
  ] as const

  const setTheme = (value: Theme) => {
    prefsForm.theme = value
    applyTheme(value)
  }
</script>

<PageTitle title="Settings" />
<div class="mx-auto w-full max-w-2xl">
  <PageHeader title="Settings" />
  <div class="mt-2 divide-y">
    <form onsubmit={(event: SubmitEvent) => { event.preventDefault(); profileForm.put('/settings/profile', { preserveScroll: true }) }}>
      <section class="grid gap-5 py-8 md:grid-cols-[200px_1fr] md:gap-10">
        <div class="space-y-1">
          <h2 class="text-base font-semibold">Profile</h2>
          <p class="text-muted-foreground text-sm">How you appear on your posts and comments.</p>
        </div>
        <div class="min-w-0 space-y-6">
          <ImageUpload bind:value={profileForm.avatarAssetId} previewUrl={user?.avatarUrl} label="Avatar" onuploadingchange={(value) => (uploadingAvatar = value)} />
          {#if profileForm.errors.avatarAssetId}<p role="alert" class="text-destructive text-sm">{profileForm.errors.avatarAssetId}</p>{/if}
          <div class="space-y-2">
            <Label for="name">Name</Label>
            <Input id="name" bind:value={profileForm.name} autocomplete="name" required aria-invalid={!!profileForm.errors.name} />
            {#if profileForm.errors.name}<p role="alert" class="text-destructive text-sm">{profileForm.errors.name}</p>{/if}
          </div>
          <div class="flex items-center gap-3">
            <Button type="submit" class="rounded-lg active:scale-[0.98]" disabled={profileForm.processing || uploadingAvatar}>
              {#if profileForm.processing}<Loader2 class="animate-spin" aria-hidden="true" />{/if}
              {profileForm.processing ? 'Saving…' : 'Save profile'}
            </Button>
            <span role="status" class="text-muted-foreground min-w-14 text-sm">
              {#if profileForm.recentlySuccessful}<span class="animate-in fade-in inline-flex items-center gap-1"><Check class="size-4" aria-hidden="true" /> Saved</span>{/if}
            </span>
          </div>
        </div>
      </section>
    </form>

    <form class="divide-y" onsubmit={(event: SubmitEvent) => { event.preventDefault(); prefsForm.put('/settings/preferences', { preserveScroll: true }) }}>
      <section class="grid gap-5 py-8 md:grid-cols-[200px_1fr] md:gap-10">
        <div class="space-y-1">
          <h2 class="text-base font-semibold">Appearance</h2>
          <p class="text-muted-foreground text-sm">Choose how the app looks on this device.</p>
        </div>
        <div class="min-w-0 space-y-6">
          <div class="space-y-3">
            <div role="radiogroup" aria-label="Theme" class="grid grid-cols-3 gap-3">
              {#each themes as t (t.value)}
                <label
                  for={`theme-${t.value}`}
                  class="group/tile flex cursor-pointer flex-col items-stretch gap-2 rounded-lg border p-2 transition-colors hover:bg-muted/50 has-[:checked]:border-foreground has-[:checked]:ring-1 has-[:checked]:ring-foreground has-[:focus-visible]:ring-[3px] has-[:focus-visible]:ring-ring/50"
                >
                  <input
                    type="radio" id={`theme-${t.value}`} name="theme" value={t.value} checked={prefsForm.theme === t.value}
                    class="sr-only" onchange={() => setTheme(t.value)}
                  />
                  <div class="relative h-16 overflow-hidden rounded-md border" aria-hidden="true">
                    <div class={cn('flex h-full w-full gap-1 p-1.5', t.value === 'dark' ? 'bg-neutral-900' : 'bg-white')}>
                      <div class={cn('w-1/4 rounded-sm', t.value === 'dark' ? 'bg-neutral-800' : 'bg-neutral-100')}></div>
                      <div class="flex flex-1 flex-col gap-1 pt-0.5">
                        <div class={cn('h-1.5 w-2/3 rounded-full', t.value === 'dark' ? 'bg-neutral-200' : 'bg-neutral-800')}></div>
                        <div class={cn('h-1 w-full rounded-full', t.value === 'dark' ? 'bg-neutral-700' : 'bg-neutral-200')}></div>
                        <div class={cn('h-1 w-4/5 rounded-full', t.value === 'dark' ? 'bg-neutral-700' : 'bg-neutral-200')}></div>
                        <div class="bg-brand mt-auto h-1.5 w-1/3 rounded-full"></div>
                      </div>
                    </div>
                    {#if t.value === 'system'}
                      <div class="absolute inset-0 [clip-path:inset(0_0_0_50%)]">
                        <div class="flex h-full w-full gap-1 bg-neutral-900 p-1.5">
                          <div class="w-1/4 rounded-sm bg-neutral-800"></div>
                          <div class="flex flex-1 flex-col gap-1 pt-0.5">
                            <div class="h-1.5 w-2/3 rounded-full bg-neutral-200"></div>
                            <div class="h-1 w-full rounded-full bg-neutral-700"></div>
                            <div class="h-1 w-4/5 rounded-full bg-neutral-700"></div>
                            <div class="bg-brand mt-auto h-1.5 w-1/3 rounded-full"></div>
                          </div>
                        </div>
                      </div>
                    {/if}
                  </div>
                  <span class="flex items-center justify-between px-0.5 text-sm font-medium">
                    {t.label}
                    <Check class="size-3.5 opacity-0 group-has-[:checked]/tile:opacity-100" aria-hidden="true" />
                  </span>
                </label>
              {/each}
            </div>
            {#if prefsForm.errors.theme}<p role="alert" class="text-destructive text-sm">{prefsForm.errors.theme}</p>{/if}
          </div>
        </div>
      </section>

      <section class="grid gap-5 py-8 md:grid-cols-[200px_1fr] md:gap-10">
        <div class="space-y-1">
          <h2 class="text-base font-semibold">Notifications</h2>
          <p class="text-muted-foreground text-sm">Choose which emails you get and how much you see per page.</p>
        </div>
        <div class="min-w-0 space-y-6">
          <div class="divide-y">
            {#each toggles as t (t.key)}
              <div class="flex items-center justify-between gap-4 py-3 first:pt-0">
                <div class="space-y-0.5">
                  <Label for={t.key}>{t.label}</Label>
                  <p id={`${t.key}-hint`} class="text-muted-foreground text-sm">{t.hint}</p>
                </div>
                <Switch
                  id={t.key}
                  checked={prefsForm[t.key]}
                  onCheckedChange={(checked: boolean) => (prefsForm[t.key] = checked)}
                  aria-describedby={`${t.key}-hint`}
                />
              </div>
            {/each}
          </div>

          <div class="space-y-2">
            <Label for="timeZoneId">Digest time zone</Label>
            <Input id="timeZoneId" bind:value={prefsForm.timeZoneId} placeholder="UTC" aria-describedby="time-zone-hint" />
            <p id="time-zone-hint" class="text-muted-foreground text-sm">Use an IANA time zone, for example Atlantic/Reykjavik.</p>
            {#if prefsForm.errors.timeZoneId}<p role="alert" class="text-destructive text-sm">{prefsForm.errors.timeZoneId}</p>{/if}
          </div>

          <div class="space-y-2 sm:max-w-48">
            <Label for="postsPerPage">Posts per page</Label>
            <Select
              id="postsPerPage"
              value={String(prefsForm.postsPerPage)}
              items={pageSizes.map(n => ({ value: String(n), label: String(n) }))}
              onValueChange={(value) => (prefsForm.postsPerPage = Number(value))}
              class="w-full"
            />
            {#if prefsForm.errors.postsPerPage}<p role="alert" class="text-destructive text-sm">{prefsForm.errors.postsPerPage}</p>{/if}
          </div>

          <div class="flex items-center gap-3">
            <Button type="submit" class="rounded-lg active:scale-[0.98]" disabled={prefsForm.processing}>
              {#if prefsForm.processing}<Loader2 class="animate-spin" aria-hidden="true" />{/if}
              {prefsForm.processing ? 'Saving…' : 'Save preferences'}
            </Button>
            <span role="status" class="text-muted-foreground min-w-14 text-sm">
              {#if prefsForm.recentlySuccessful}<span class="animate-in fade-in inline-flex items-center gap-1"><Check class="size-4" aria-hidden="true" /> Saved</span>{/if}
            </span>
          </div>
        </div>
      </section>
    </form>
  </div>
</div>
