<script setup lang="ts">
import { Head, useForm } from '@inertiajs/vue3'
import PageHeader from '@/components/PageHeader.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import type { UserPreferences } from '@/types'

const props = defineProps<{
  profile: { name: string; email: string; avatarAssetId: string | null }
  preferences: UserPreferences
}>()

const profileForm = useForm({ name: props.profile.name, avatarAssetId: props.profile.avatarAssetId })
const prefsForm = useForm({
  emailOrderUpdates: props.preferences.emailOrderUpdates,
  emailInstallmentReminders: props.preferences.emailInstallmentReminders,
  emailSupportReplies: props.preferences.emailSupportReplies,
  emailPromos: props.preferences.emailPromos,
  timeZoneId: props.preferences.timeZoneId,
})

const saveProfile = () => profileForm.put('/account/profile')
const savePrefs = () => prefsForm.put('/account/preferences')

const prefRows = [
  { key: 'emailOrderUpdates' as const, label: 'Order updates', hint: 'Receipts, dispatch, delivery — every timeline hop.' },
  { key: 'emailInstallmentReminders' as const, label: 'Installment reminders', hint: 'Due-soon notices three days ahead, and the weekly statement.' },
  { key: 'emailSupportReplies' as const, label: 'Support replies', hint: 'When staff answers a ticket.' },
  { key: 'emailPromos' as const, label: 'Deals & promos', hint: 'Weekend sales. You can always unsubscribe from the mail itself.' },
]
</script>

<template>
  <Head title="Preferences" />
    <Head title="Preferences" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Profile & preferences" description="Who you are, and what we're allowed to put in your inbox." />

    <div class="grid gap-5 lg:grid-cols-2">
      <Card>
        <CardHeader><CardTitle class="font-display text-base">Profile</CardTitle></CardHeader>
        <CardContent>
          <form class="flex flex-col gap-3" @submit.prevent="saveProfile">
            <div class="grid gap-1.5">
              <Label for="p-name">Display name</Label>
              <Input id="p-name" v-model="profileForm.name" required minlength="2" maxlength="80" />
              <span v-if="profileForm.errors.name" class="text-destructive text-xs">{{ profileForm.errors.name }}</span>
            </div>
            <div class="grid gap-1.5">
              <Label for="p-email">Email</Label>
              <Input id="p-email" :model-value="props.profile.email" disabled />
              <p class="text-muted-foreground text-xs">Email changes need re-verification — contact support for that.</p>
            </div>
            <Button type="submit" :disabled="profileForm.processing" class="w-fit">Save profile</Button>
          </form>
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle class="font-display text-base">Notifications</CardTitle></CardHeader>
        <CardContent>
          <form class="flex flex-col gap-4" @submit.prevent="savePrefs">
            <div class="flex flex-col gap-3">
              <label v-for="row in prefRows" :key="row.key" class="flex items-center justify-between gap-4">
                <span class="min-w-0">
                  <span class="block text-sm font-medium">{{ row.label }}</span>
                  <span class="text-muted-foreground block text-xs leading-5">{{ row.hint }}</span>
                </span>
                <Switch :model-value="prefsForm[row.key]" @update:model-value="(v: boolean) => (prefsForm[row.key] = v)" />
              </label>
            </div>
            <div class="grid gap-1.5">
              <Label for="p-tz">Time zone (reminders are delivered on your clock)</Label>
              <Select v-model="prefsForm.timeZoneId">
                <SelectTrigger id="p-tz"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="UTC">UTC</SelectItem>
                  <SelectItem value="GMT">GMT (Accra)</SelectItem>
                  <SelectItem value="W. Europe Standard Time">W. Europe</SelectItem>
                  <SelectItem value="Eastern Standard Time">Eastern (US)</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <Button type="submit" :disabled="prefsForm.processing" class="w-fit">Save preferences</Button>
          </form>
        </CardContent>
      </Card>
    </div>
  </div>
</template>
