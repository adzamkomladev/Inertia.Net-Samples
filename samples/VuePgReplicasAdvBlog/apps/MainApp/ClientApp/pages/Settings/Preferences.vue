<script setup lang="ts">
import { Head, useForm, usePage } from '@inertiajs/vue3'
import { ref } from 'vue'
import { Check, Loader2 } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import PageHeader from '@/components/PageHeader.vue'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import ImageUpload from '@/components/ImageUpload.vue'
import { applyTheme } from '@/composables/useTheme'
import type { Preferences, Theme } from '@/types'

const props = defineProps<{
  preferences: Preferences
  profile: { name: string; avatarAssetId: string | null }
}>()

const page = usePage()
const uploadingAvatar = ref(false)

const profileForm = useForm({ name: props.profile.name, avatarAssetId: props.profile.avatarAssetId })
const prefsForm = useForm({ ...props.preferences, dailyRecommendationDigest: props.preferences.dailyRecommendationDigest ?? false, timeZoneId: props.preferences.timeZoneId ?? 'UTC' })

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

const setTheme = (v: unknown) => {
  prefsForm.theme = v as Theme
  applyTheme(prefsForm.theme)
}
</script>

<template>
  <Head title="Settings" />
  <div class="mx-auto w-full max-w-2xl">
    <PageHeader title="Settings" />
    <div class="mt-2 divide-y">
      <form @submit.prevent="profileForm.put('/settings/profile', { preserveScroll: true })">
        <section class="grid gap-5 py-8 md:grid-cols-[200px_1fr] md:gap-10">
          <div class="space-y-1">
            <h2 class="text-base font-semibold">Profile</h2>
            <p class="text-sm text-muted-foreground">How you appear on your posts and comments.</p>
          </div>
          <div class="min-w-0 space-y-6">
            <ImageUpload v-model="profileForm.avatarAssetId" :preview-url="page.props.auth.user?.avatarUrl" label="Avatar" @uploading="uploadingAvatar = $event" />
            <p v-if="profileForm.errors.avatarAssetId" role="alert" class="text-sm text-destructive">{{ profileForm.errors.avatarAssetId }}</p>
            <div class="space-y-2">
              <Label for="name">Name</Label>
              <Input id="name" v-model="profileForm.name" autocomplete="name" required :aria-invalid="!!profileForm.errors.name" />
              <p v-if="profileForm.errors.name" role="alert" class="text-sm text-destructive">{{ profileForm.errors.name }}</p>
            </div>
            <div class="flex items-center gap-3">
              <Button type="submit" class="rounded-lg active:scale-[0.98]" :disabled="profileForm.processing || uploadingAvatar">
                <Loader2 v-if="profileForm.processing" class="animate-spin" aria-hidden="true" />
                {{ profileForm.processing ? 'Saving…' : 'Save profile' }}
              </Button>
              <span role="status" class="min-w-14 text-sm text-muted-foreground">
                <span v-if="profileForm.recentlySuccessful" class="animate-in inline-flex items-center gap-1 fade-in"><Check class="size-4" aria-hidden="true" /> Saved</span>
              </span>
            </div>
          </div>
        </section>
      </form>

      <form class="divide-y" @submit.prevent="prefsForm.put('/settings/preferences', { preserveScroll: true })">
        <section class="grid gap-5 py-8 md:grid-cols-[200px_1fr] md:gap-10">
          <div class="space-y-1">
            <h2 class="text-base font-semibold">Appearance</h2>
            <p class="text-sm text-muted-foreground">Choose how the app looks on this device.</p>
          </div>
          <div class="min-w-0 space-y-6">
            <div class="space-y-3">
              <RadioGroup aria-label="Theme" :model-value="prefsForm.theme" class="grid grid-cols-3 gap-3" @update:model-value="setTheme">
                <Label
                  v-for="t in themes"
                  :key="t.value"
                  :for="`theme-${t.value}`"
                  class="group/tile flex cursor-pointer flex-col items-stretch gap-2 rounded-lg border p-2 transition-colors hover:bg-muted/50 has-[[data-state=checked]]:border-foreground has-[[data-state=checked]]:ring-1 has-[[data-state=checked]]:ring-foreground has-[:focus-visible]:ring-[3px] has-[:focus-visible]:ring-ring/50"
                >
                  <RadioGroupItem :id="`theme-${t.value}`" :value="t.value" class="sr-only" />
                  <div class="relative h-16 overflow-hidden rounded-md border" aria-hidden="true">
                    <div class="flex h-full w-full gap-1 p-1.5" :class="t.value === 'dark' ? 'bg-neutral-900' : 'bg-white'">
                      <div class="w-1/4 rounded-sm" :class="t.value === 'dark' ? 'bg-neutral-800' : 'bg-neutral-100'" />
                      <div class="flex flex-1 flex-col gap-1 pt-0.5">
                        <div class="h-1.5 w-2/3 rounded-full" :class="t.value === 'dark' ? 'bg-neutral-200' : 'bg-neutral-800'" />
                        <div class="h-1 w-full rounded-full" :class="t.value === 'dark' ? 'bg-neutral-700' : 'bg-neutral-200'" />
                        <div class="h-1 w-4/5 rounded-full" :class="t.value === 'dark' ? 'bg-neutral-700' : 'bg-neutral-200'" />
                        <div class="mt-auto h-1.5 w-1/3 rounded-full bg-brand" />
                      </div>
                    </div>
                    <div v-if="t.value === 'system'" class="absolute inset-0 [clip-path:inset(0_0_0_50%)]">
                      <div class="flex h-full w-full gap-1 bg-neutral-900 p-1.5">
                        <div class="w-1/4 rounded-sm bg-neutral-800" />
                        <div class="flex flex-1 flex-col gap-1 pt-0.5">
                          <div class="h-1.5 w-2/3 rounded-full bg-neutral-200" />
                          <div class="h-1 w-full rounded-full bg-neutral-700" />
                          <div class="h-1 w-4/5 rounded-full bg-neutral-700" />
                          <div class="mt-auto h-1.5 w-1/3 rounded-full bg-brand" />
                        </div>
                      </div>
                    </div>
                  </div>
                  <span class="flex items-center justify-between px-0.5 text-sm font-medium">
                    {{ t.label }}
                    <Check class="size-3.5 opacity-0 group-has-[[data-state=checked]]/tile:opacity-100" aria-hidden="true" />
                  </span>
                </Label>
              </RadioGroup>
              <p v-if="prefsForm.errors.theme" role="alert" class="text-sm text-destructive">{{ prefsForm.errors.theme }}</p>
            </div>
          </div>
        </section>

        <section class="grid gap-5 py-8 md:grid-cols-[200px_1fr] md:gap-10">
          <div class="space-y-1">
            <h2 class="text-base font-semibold">Notifications</h2>
            <p class="text-sm text-muted-foreground">Choose which emails you get and how much you see per page.</p>
          </div>
          <div class="min-w-0 space-y-6">
            <div class="divide-y">
              <div v-for="t in toggles" :key="t.key" class="flex items-center justify-between gap-4 py-3 first:pt-0">
                <div class="space-y-0.5">
                  <Label :for="t.key">{{ t.label }}</Label>
                  <p :id="`${t.key}-hint`" class="text-sm text-muted-foreground">{{ t.hint }}</p>
                </div>
                <Switch :id="t.key" v-model="prefsForm[t.key]" :aria-describedby="`${t.key}-hint`" />
              </div>
            </div>

            <div class="space-y-2">
              <Label for="timeZoneId">Digest time zone</Label>
              <Input id="timeZoneId" v-model="prefsForm.timeZoneId" placeholder="UTC" aria-describedby="time-zone-hint" />
              <p id="time-zone-hint" class="text-sm text-muted-foreground">Use an IANA time zone, for example Atlantic/Reykjavik.</p>
              <p v-if="prefsForm.errors.timeZoneId" role="alert" class="text-sm text-destructive">{{ prefsForm.errors.timeZoneId }}</p>
            </div>

            <div class="space-y-2 sm:max-w-48">
              <Label for="postsPerPage">Posts per page</Label>
              <Select :model-value="String(prefsForm.postsPerPage)" @update:model-value="(v) => (prefsForm.postsPerPage = Number(v))">
                <SelectTrigger id="postsPerPage" class="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="n in pageSizes" :key="n" :value="String(n)">{{ n }}</SelectItem>
                </SelectContent>
              </Select>
              <p v-if="prefsForm.errors.postsPerPage" role="alert" class="text-sm text-destructive">{{ prefsForm.errors.postsPerPage }}</p>
            </div>

            <div class="flex items-center gap-3">
              <Button type="submit" class="rounded-lg active:scale-[0.98]" :disabled="prefsForm.processing">
                <Loader2 v-if="prefsForm.processing" class="animate-spin" aria-hidden="true" />
                {{ prefsForm.processing ? 'Saving…' : 'Save preferences' }}
              </Button>
              <span role="status" class="min-w-14 text-sm text-muted-foreground">
                <span v-if="prefsForm.recentlySuccessful" class="animate-in inline-flex items-center gap-1 fade-in"><Check class="size-4" aria-hidden="true" /> Saved</span>
              </span>
            </div>
          </div>
        </section>
      </form>
    </div>
  </div>
</template>