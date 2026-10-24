<script setup lang="ts">
import { computed } from 'vue'
import { useIntervalFn, useMounted, useNow } from '@vueuse/core'
import { Clock, Mail, MapPin, Phone, Truck } from '@lucide/vue'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { cn } from '@/lib/utils'
import type { StoreSettings } from '@/types/management'

const props = defineProps<{ settings: StoreSettings }>()

const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

// Live status is computed after mount in the store's own time zone, so SSR and hydration render the same neutral text.
const mounted = useMounted()
const now = useNow({ scheduler: (tick) => useIntervalFn(tick, 60_000) })

/** Weekday (0 = Sunday) and "HH:mm" for `at` in the store's time zone. */
function storeClock(at: Date) {
  let parts: Intl.DateTimeFormatPart[]
  try {
    parts = new Intl.DateTimeFormat('en-US', { timeZone: props.settings.timeZoneId, weekday: 'short', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).formatToParts(at)
  } catch {
    return null // unknown zone id: show the weekly table without a live status
  }
  const get = (t: string) => parts.find((p) => p.type === t)?.value ?? ''
  return { day: DAYS.indexOf(get('weekday')), time: `${get('hour')}:${get('minute')}` }
}

const status = computed(() => {
  const clock = mounted.value ? storeClock(now.value) : null
  if (!clock) return null
  const { day, time } = clock
  const today = props.settings.hours.find((h) => h.day === day)
  const open = !!today && !today.closed && time >= today.opens && time < today.closes
  const delivering = !!today && !today.deliveryClosed && time >= today.deliveryOpens && time < today.deliveryCloses
  // Next opening: later today, else the first open day after today.
  let next = ''
  if (!open) {
    if (today && !today.closed && time < today.opens) next = `opens ${today.opens}`
    else for (let i = 1; i <= 7; i++) {
      const h = props.settings.hours.find((x) => x.day === (day + i) % 7)
      if (h && !h.closed) { next = `opens ${i === 1 ? 'tomorrow' : DAYS[h.day]} ${h.opens}`; break }
    }
  }
  return { day, open, delivering, today, next }
})
</script>

<!-- Live store status strip with the full week in a popover. -->
<template>
  <div class="surface flex flex-wrap items-center gap-x-5 gap-y-2 px-4 py-3 text-sm">
    <p class="flex items-center gap-2 font-medium">
      <span class="relative flex size-2.5" aria-hidden="true">
        <span v-if="status?.open" class="bg-success absolute inline-flex size-full animate-ping rounded-full opacity-60 motion-reduce:hidden" />
        <span :class="cn('relative inline-flex size-2.5 rounded-full', status ? (status.open ? 'bg-success' : 'bg-muted-foreground/50') : 'bg-muted')" />
      </span>
      <template v-if="status">{{ status.open ? `Open now · until ${status.today!.closes}` : `Closed · ${status.next || 'see hours'}` }}</template>
      <template v-else>{{ settings.name }} hours</template>
    </p>
    <p v-if="status" class="text-muted-foreground flex items-center gap-1.5">
      <Truck class="size-4" aria-hidden="true" />
      {{ status.today && !status.today.deliveryClosed ? `Deliveries today ${status.today.deliveryOpens}–${status.today.deliveryCloses}` : 'No deliveries today' }}
    </p>
    <Popover>
      <PopoverTrigger class="text-primary ml-auto flex items-center gap-1.5 rounded-full text-sm font-medium hover:underline">
        <Clock class="size-4" aria-hidden="true" /> Weekly hours
      </PopoverTrigger>
      <PopoverContent align="end" class="w-80">
        <p class="font-medium">{{ settings.name }}</p>
        <p class="text-muted-foreground text-xs">Times in {{ settings.timeZoneId.replace(/_/g, ' ') }}. Order any time; fulfilment follows these hours.</p>
        <table class="mt-3 w-full text-left text-xs">
          <thead class="text-muted-foreground">
            <tr><th class="pb-1.5 font-medium">Day</th><th class="pb-1.5 font-medium">Store</th><th class="pb-1.5 font-medium">Delivery</th></tr>
          </thead>
          <tbody>
            <tr v-for="h in settings.hours" :key="h.day" :class="cn('border-t', status?.day === h.day && 'text-primary font-medium')">
              <th class="py-1.5 font-medium">{{ DAYS[h.day] }}</th>
              <td class="py-1.5 tabular-nums">{{ h.closed ? 'Closed' : `${h.opens}–${h.closes}` }}</td>
              <td class="py-1.5 tabular-nums">{{ h.deliveryClosed ? '—' : `${h.deliveryOpens}–${h.deliveryCloses}` }}</td>
            </tr>
          </tbody>
        </table>
        <div class="text-muted-foreground mt-3 space-y-1 border-t pt-3 text-xs">
          <p v-if="settings.address" class="flex items-center gap-1.5"><MapPin class="size-3.5" aria-hidden="true" />{{ settings.address }}</p>
          <a :href="`mailto:${settings.email}`" class="hover:text-foreground flex items-center gap-1.5"><Mail class="size-3.5" aria-hidden="true" />{{ settings.email }}</a>
          <a v-if="settings.phone" :href="`tel:${settings.phone}`" class="hover:text-foreground flex items-center gap-1.5"><Phone class="size-3.5" aria-hidden="true" />{{ settings.phone }}</a>
        </div>
      </PopoverContent>
    </Popover>
  </div>
</template>
