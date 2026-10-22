<script setup lang="ts">
import { Head, useForm } from '@inertiajs/vue3'
import PageHeader from '@/components/PageHeader.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import type { StoreSettings } from '@/types/management'
const props = defineProps<{ settings: StoreSettings; timeZones: string[] }>()
const form = useForm({
  ...props.settings,
  hours: props.settings.hours.map((hours) => ({ ...hours })),
})
const days = [
  'Sunday',
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
]
const times = Array.from(
  { length: 48 },
  (_, i) =>
    `${String(Math.floor(i / 2)).padStart(2, '0')}:${i % 2 ? '30' : '00'}`,
)
</script>
<template>
  <Head title="Store settings" />
  <div class="space-y-6">
    <PageHeader
      title="Store settings"
      description="Manage contact information, working hours and delivery windows shown to customers."
    />
    <form
      class="space-y-6"
      @submit.prevent="form.post('/admin/settings', { preserveScroll: true })"
    >
      <section class="surface grid gap-5 p-6 sm:grid-cols-2">
        <div class="space-y-2">
          <Label for="store-name">Store name</Label
          ><Input id="store-name" v-model="form.name" required maxlength="80" />
        </div>
        <div class="space-y-2">
          <Label for="store-email">Support email</Label
          ><Input
            id="store-email"
            v-model="form.email"
            type="email"
            required
            maxlength="254"
          />
        </div>
        <div class="space-y-2">
          <Label for="store-phone">Phone</Label
          ><Input
            id="store-phone"
            v-model="form.phone"
            type="tel"
            maxlength="30"
          />
        </div>
        <div class="space-y-2">
          <Label for="store-address">Store address</Label
          ><Input id="store-address" v-model="form.address" maxlength="300" />
        </div>
        <div class="space-y-2 sm:col-span-2">
          <Label for="store-zone">Time zone for all hours</Label
          ><Select v-model="form.timeZoneId"
            ><SelectTrigger id="store-zone"><SelectValue /></SelectTrigger
            ><SelectContent
              ><SelectItem
                v-for="zone in timeZones"
                :key="zone"
                :value="zone"
                >{{ zone }}</SelectItem
              ></SelectContent
            ></Select
          >
        </div>
      </section>
      <section class="surface space-y-5 p-6">
        <div>
          <h2 class="text-lg font-semibold">Weekly schedule</h2>
          <p class="text-sm text-muted-foreground">
            Orders can be placed anytime. Fulfilment and support follow these
            hours. Windows end on the same day.
          </p>
        </div>
        <div
          v-for="h in form.hours"
          :key="h.day"
          class="grid gap-4 border-t pt-5 lg:grid-cols-[7rem_1fr_1fr]"
        >
          <h3 class="font-semibold">{{ days[h.day] }}</h3>
          <div class="space-y-3">
            <div class="flex items-center gap-3">
              <Switch
                :id="`closed-${h.day}`"
                :model-value="!h.closed"
                @update:model-value="h.closed = !$event"
              /><Label :for="`closed-${h.day}`">{{
                h.closed ? 'Store closed' : 'Store open'
              }}</Label>
            </div>
            <div v-if="!h.closed" class="grid grid-cols-2 gap-3">
              <div
                v-for="field in ['opens', 'closes'] as const"
                :key="field"
                class="space-y-1"
              >
                <Label :for="`${field}-${h.day}`">{{
                  field === 'opens' ? 'Opens' : 'Closes'
                }}</Label
                ><Select v-model="h[field]"
                  ><SelectTrigger :id="`${field}-${h.day}`"
                    ><SelectValue /></SelectTrigger
                  ><SelectContent
                    ><SelectItem
                      v-for="time in times"
                      :key="time"
                      :value="time"
                      >{{ time }}</SelectItem
                    ></SelectContent
                  ></Select
                >
              </div>
            </div>
          </div>
          <div class="space-y-3">
            <div class="flex items-center gap-3">
              <Switch
                :id="`delivery-${h.day}`"
                :model-value="!h.deliveryClosed"
                @update:model-value="h.deliveryClosed = !$event"
              /><Label :for="`delivery-${h.day}`">{{
                h.deliveryClosed ? 'No deliveries' : 'Deliveries available'
              }}</Label>
            </div>
            <div v-if="!h.deliveryClosed" class="grid grid-cols-2 gap-3">
              <div
                v-for="field in ['deliveryOpens', 'deliveryCloses'] as const"
                :key="field"
                class="space-y-1"
              >
                <Label :for="`${field}-${h.day}`">{{
                  field === 'deliveryOpens' ? 'From' : 'Until'
                }}</Label
                ><Select v-model="h[field]"
                  ><SelectTrigger :id="`${field}-${h.day}`"
                    ><SelectValue /></SelectTrigger
                  ><SelectContent
                    ><SelectItem
                      v-for="time in times"
                      :key="time"
                      :value="time"
                      >{{ time }}</SelectItem
                    ></SelectContent
                  ></Select
                >
              </div>
            </div>
          </div>
        </div>
      </section>
      <p
        v-for="(error, field) in form.errors"
        :key="field"
        role="alert"
        class="text-sm text-destructive"
      >
        {{ error }}
      </p>
      <Button type="submit" :disabled="form.processing">{{
        form.processing ? 'Saving...' : 'Save settings'
      }}</Button>
    </form>
  </div>
</template>
